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

- **Loops share nothing.** Two event loop threads, each with its own `SO_REUSEPORT` listener, clients, Postgres
  connections and queues. Nothing on the request path takes a lock.
- **An idle connection costs almost nothing.** It holds no buffers, just a small object and a socket. Receives go into
  one buffer per loop, and requests are parsed in place. A job copies only the strings it keeps while it waits.
- **Reads run as batches.** Requests gather for up to 5 ms, or until a read connection is free after that. Then each
  kind of read becomes one statement over arrays of the batch's distinct keys, in up to three round trips:
  - A: users, slugs, pages of ids and capped counts.
  - B: each distinct article once, comments, favorited pairs, and one read of the tags table.
  - C: `(viewer, author)` pairs, once the authors are known.
- **Rows are not objects.** A reply is gathered whole in one buffer, and JSON is written straight from the wire bytes.
- **Writes commit in groups.** All waiting writes go out as a pipeline ending in one Sync, which Postgres runs as one
  transaction with one WAL flush. Each write is one statement that reports "not found" or "not yours" as data, so normal
  outcomes can't abort the group. If a statement does fail, nothing was committed and the writes run again one at a time.
- **bcrypt runs four passwords at a time.** Four threads run four interleaved Blowfish chains in one loop. A password
  waits up to 100 ms for company. Results return through an inbox and an eventfd.
- **No tables of constants.** SHA-256's constants and Blowfish's digits of pi are computed from their definitions at
  startup.

## How it keeps the rules

- **No caching across requests.** Requests in one batch share rows Postgres returned while they waited. Nothing is kept
  once the batch has answered.
- **Writes are committed before the response.** Each one is answered after the ReadyForQuery that follows its commit.
  Responses showing an article come from a read that starts after that commit.
- **bcrypt cost 10** for every new password, and every login verifies against the stored hash.
- **HS256 signature, algorithm and `exp`** are checked on every request; the signature is compared in encoded form.
- **10 Postgres connections**: 4 readers and 1 writer per loop. The settings are session-level, sent at connect:
  `plan_cache_mode`, `jit`, parallel workers off, and on readers the `enable_*` switches that leave only index lookups.

## Measured

Local only, on the 2% dev seed, so **not a prediction of a leaderboard score**: the data is 50 times smaller than the
full seed, and everything shared 6 cores (`--cpuset-cpus=16-21`) in Docker on WSL2, load generator included. Postgres ran
in its own container with `shared_buffers=2GB`. Runs: `-ramp 10s -measure 60s -tail 5s -think-scale 0.1 -articles 10000`.

| Users | req/s | p50 ms | p95 ms | p99 ms | Errors | Verdict |
|--:|--:|--:|--:|--:|--:|---|
| 5,000 | 13,033 | 5.2 | 8.6 | 13.8 | 0 | PASS |
| 20,000 | 48,875 | 25.0 | 66.7 | 105.7 | 0 | PASS |
| 40,000 | 44,993 | 443.4 | 807.4 | 1,074.2 | 0 | FAIL (latency) |

`-think-scale 0.1` also runs logins at 10 times their normal rate per user, so at 40,000 users bcrypt takes a large
share of the cores. With 2 bcrypt threads instead of 4, that load reached 54,564 req/s at p95 557 ms, but 5,077 logins
and sign-ups timed out (and the load generator fell behind in that run). 4 threads was chosen for no errors.

`bash run.sh` repeats CI's checks in Docker; `bash run.sh up` then `bash run.sh load 5000` repeats a load test.

## Known limits

- A failed read batch answers 500 to all its requests rather than retrying them. Keys Postgres can't store (NUL, invalid
  UTF-8) are stopped before they reach a batch, so no request should be able to cause this.
- Requests with `Transfer-Encoding: chunked` get 501.
- A login for an unknown email skips bcrypt, so its timing shows the email doesn't exist.
- `HOST` and the database host must be IPv4 addresses or `localhost`.
- Not yet run against the full seed. Query plans, the 5 ms gather window and the thread counts are untuned for it.

## Settings

The defaults need no configuration. For experiments: `BARE_LOOPS` (2), `BARE_READ_CONNS` (4 per loop),
`BARE_WRITE_CONNS` (1 per loop), `BARE_BATCH_MAX` (1024), `BARE_GATHER_MS` (5), `BARE_WRITE_GROUP` (128),
`BARE_BCRYPT_THREADS` (4), `BARE_BCRYPT_WAIT_MS` (100).

## Credits

The ideas of batching reads over distinct keys in phases, group commit, interleaved bcrypt and counting the global list
from the oldest end of its key come from `csharp-epoll-aot-gichamba`, `csharp-epoll-xeonfx` and the entries they credit. The code here is
written from scratch.
