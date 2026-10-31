# csharp-bare-mrroza

Front: direct

Conduit in essentially raw .NET: no web framework, no database driver, no packages.

| | |
|---|---|
| Language | C# on .NET 10 (SDK 10.0.402, pinned by `global.json`), compiled with Native AOT for x86-64-v3 |
| HTTP | own HTTP/1.1 on edge-triggered epoll, called straight through libc |
| Postgres | own client for protocol 3.0: prepared statements, pipelines, binary rows, SCRAM-SHA-256 |
| bcrypt, HS256 | own code, cost 10 |
| Packages | none; restore fetches only the Native AOT compiler the pinned SDK names |

## How it works

- **Network threads and one Postgres thread.** Four network threads, each with its own `SO_REUSEPORT` listener and clients,
  parse requests and check tokens. One thread owns all Postgres connections, so every request in the process shares one
  read queue and one write queue. Inboxes are lock-free lists, and a push only wakes a thread that nothing else would wake.
- **The network threads write the responses.** A finished batch goes back to them as packets of their own requests. They
  read its rows directly, without copying or locks, and the last one done returns the batch for reuse.
- **An idle connection costs almost nothing.** It holds no buffers, just a small object and a socket. Receives go into
  one buffer per thread, and requests are parsed in place. A job copies only the strings it keeps while it waits.
- **Reads run as batches.** Requests gather for up to 20 ms, or until a read connection is free after that. Each kind of
  read then becomes one statement over the batch's distinct keys, in up to five round trips:
  - A: users, articles, comments, pages of ids and capped counts.
  - Two more for feeds, below.
  - B: tags and favorite counts of each distinct article, its row if a list named it, the viewers' favorites, and one read
    of the tags table.
  - C: each distinct author, and the viewers' follows.
- **Articles are found by primary key.** Every slug ends in its article's id, in the seed and in every write. So a slug
  costs an integer lookup instead of a descent of the text index, and the row's slug is then compared with the one asked
  for. Rows are never read twice to get the body.
- **One index scan instead of a loop of them.** Lookups by id use `= ANY(array)`, which doesn't restart a scan per key and
  doesn't descend again when the next key is on the same page. A viewer's favorites and follows are one scan per viewer.
- **Feeds are planned here, not in SQL.** After reading each viewer's followees, the server reads the followees most
  viewers share once, with their newest ids. The id of one's (limit + offset)-th newest article is a floor for that
  viewer's page, because that followee alone fills the page with articles at least that new. All other followees come from
  one scan down to the floor, and each page is merged here. The weakest quarter of the floors take the reference statement
  instead, so they don't lengthen everyone's scan. A followee with 1,000 articles settles the count. Every author in a feed
  is followed, so those flags aren't read.
- **Rows are not objects.** A reply is gathered whole in one buffer, and JSON is written straight from the wire bytes.
- **Writes commit in groups.** All waiting writes go out as a pipeline ending in one Sync, which Postgres runs as one
  transaction with one WAL flush. Each write is one statement that reports "not found" or "not yours" as data, so normal
  outcomes can't abort the group. If a statement does fail, nothing was committed and the writes run again one at a time.
- **bcrypt runs five passwords at a time.** Each thread runs five interleaved Blowfish chains in one loop. All lanes sit at
  constant offsets from one pointer and the rounds are written out, so everything stays in registers. That took a hash
  from 46 ms alone to 13 ms in lanes. A password waits up to 150 ms for company.
- **Threads stay out of the way.** Network and Postgres threads run under `SCHED_BATCH`, so their wake-ups don't preempt
  busy backends, and each has its own descriptor table (`unshare(CLONE_FILES)`), so descriptor lookups are not shared.
- **No tables of constants.** SHA-256's constants and Blowfish's digits of pi are computed from their definitions at
  startup.

## How it keeps the rules

- **No caching across requests.** Requests in one batch share rows Postgres returned while they waited. Nothing is kept
  once the batch has answered.
- **Writes are committed before the response.** Each one is answered after the ReadyForQuery that follows its commit.
  Responses showing an article come from a read that starts after that commit.
- **bcrypt cost 10** for every new password, and every login verifies against the stored hash.
- **HS256 signature, algorithm and `exp`** are checked on every request; the signature is compared in encoded form.
- **9 Postgres connections**: 8 readers and 1 writer. The settings are session-level, sent at connect: `plan_cache_mode`,
  `jit`, parallel workers off, and on readers the `enable_*` switches that leave only index lookups.

## Measured

Local only, so **not a prediction of a leaderboard score**. Full seed, in Docker on WSL2 on a Ryzen 9 5900X: server and
Postgres on 8 CPUs and 16 GB, the load generator on 8 others over loopback, `-think-scale 0.1`, 60 s measured. Each row of
the first table ran in the same half hour, while another job was also using the machine; absolute numbers ran up to about
30% higher on a quiet machine, so only compare within a table.

| 44,000 users | req/s | p50 ms | p95 ms | p99 ms | Errors |
|---|--:|--:|--:|--:|--:|
| this version | 62,707 | 314 | 544 | 663 | 0 |
| this version, repeat | 61,560 | 328 | 571 | 682 | 0 |
| `rust-epoll-theCatWisel` (leaderboard #1) | 58,084 | 363 | 670 | 786 | 0 |
| the previous version of this entry | 37,819 | 785 | 963 | 1,062 | 0.07% (login timeouts) |

A 5-minute hold at 36,000 users gave 59,938 req/s at p50 212 ms, p95 396 ms, p99 486 ms, with no errors. Ending all
Postgres connections during a run at 10,000 users cost 0.12% of its requests a 503; the server reconnected without help.

Lowering bcrypt's priority raises these numbers, but under heavy load it times logins out, so it stays off by default.

`bash run.sh` repeats CI's checks in Docker; `bash run.sh up` then `bash run.sh load 5000` repeats a load test on the dev seed.

## Known limits

- A failed read batch answers 500 to all its requests rather than retrying them. Keys Postgres can't store (NUL, invalid
  UTF-8) are stopped before they reach a batch, so no request should be able to cause this.
- Requests with `Transfer-Encoding: chunked` get 501.
- A login for an unknown email skips bcrypt, so its timing shows the email doesn't exist.
- `HOST` and the database host must be IPv4 addresses or `localhost`.
- Above about 64,000 connections the local rig runs out of ports, so holding more connections than that is untested. The
  thread counts and the gather window were tuned over loopback, not a real network.

## Settings

The defaults need no configuration. For experiments: `BARE_LOOPS` (4), `BARE_READ_CONNS` (8), `BARE_WRITE_CONNS` (1),
`BARE_BATCH_MAX` (4096), `BARE_GATHER_MS` (20), `BARE_WRITE_GROUP` (256), `BARE_SCHED_BATCH` (1),
`BARE_BCRYPT_THREADS` (6), `BARE_BCRYPT_WAIT_MS` (150), `BARE_BCRYPT_NICE` (0).

## Credits

The ideas of batching reads over distinct keys in phases, group commit, interleaved bcrypt and counting the global list
from the oldest end of its key come from `csharp-epoll-aot-gichamba`, `csharp-epoll-xeonfx` and the entries they credit.
One queue for the whole process comes from `zig-iouring-nick-potts`. Shared followees as a floor for feeds, `= ANY` per
viewer for flags, private descriptor tables and `SCHED_BATCH` come from `rust-epoll-theCatWisel` and
`rust-epoll-WorriedArrow`. The code here is written from scratch.
