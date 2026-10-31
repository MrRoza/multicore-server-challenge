# Session notes, 2026-10-30 to 2026-10-31

How this submission got to its current shape, and what was measured along the way.

## Where it stands

- Branch `add-csharp-simple-mrroza`, which also holds the separate `csharp-bare-mrroza` submission.
- Pushed up to `7739093`. The rule from now on: commit only, don't push.
- No pull request is open, so the submission isn't entered in the challenge yet.
- The last run passed all 82 integration tests and all 197 conformance checks.

## How the code evolved

| Commit | Change |
|---|---|
| `d7a7835` | First version: a regular ASP.NET Core minimal API with Dapper and the standard JWT handler |
| `0988dac` | Feature slices and CQRS |
| `5485726` | Integration tests that start the app and send real HTTP requests, with Postgres 18 in a container |
| `7c185de` | A small dispatcher, and an `Add<Slice>` method per slice for handlers and routes |
| `f29b325` | One type per file, `Commands`, `Queries`, `Requests` and `Responses` folders, `Shared/Cqrs` and `Shared/Auth` |
| `6385a1d` | Separate processes behind a gateway, over gRPC, plus `run.sh` |
| `5f72abb` | Fixed a connection pool stall, and bracketed `using` blocks everywhere |
| `7739093` | `Dto` suffix on data classes |
| `b647cb0` | Five layered projects per slice, and a unit of work per request |
| `4e11db9` | `Conduit.Shared.Data.Postgres` with a base repository, and a connection taken on the first query |
| `6e6079d` | Back to one process with `Conduit.Host` and in-process clients. Gateway and gRPC removed |
| `bed32c1` | Fewer and cheaper database calls: prepared statements, no connection reset, batched round trips |

## Current design

- `Conduit.Host` is the one process. It has a connection pool of 90, and `Conduit.Http` holds its routes.
- Each slice has three projects: `Abstractions`, core (commands, queries, handlers, client) and `Data.Postgres`.
- A route calls a slice's client, which begins the unit of work and dispatches a command or query.
- A handler dispatched from another handler shares the request's unit of work, through the request's DI scope.
- The README has the project table.

## Code conventions asked for

- One type per file, except that a command or query shares a file with its handler.
- Bracketed `using (...) { }` blocks, never `using var`.
- A `Dto` suffix on classes that only carry data.
- No assembly scanning to register slices: each one is listed by hand.

## Benchmarks

Each run was 5,000 users for 60 seconds on the 2% dev seed, with users 10x as active as normal. The server and Postgres
shared one container with 8 CPUs and 16 GB, and the load generator ran on 8 other CPUs. These numbers compare versions
with each other. They aren't leaderboard scores.

| Version | Requests/s | p50 | p95 | p99 | Errors | Memory |
|---|---:|---:|---:|---:|---:|---:|
| One process, with the stall (`7c185de`) | 3,654 | 223 ms | 10,002 ms | 10,002 ms | 8.0% | 1.2 GB |
| gRPC, with the stall (`6385a1d`) | 887 | 92 ms | 10,002 ms | 10,002 ms | 47.0% | 2.4 GB |
| gRPC, stall fixed (`5f72abb`) | 7,451 | 282 ms | 633 ms | 855 ms | 0% | 1.9 GB |
| gRPC, unit of work (`b647cb0`) | 7,629 | 306 ms | 517 ms | 589 ms | 0% | 2.5 GB |
| gRPC, base repository (`4e11db9`) | 7,752 | 284 ms | 521 ms | 599 ms | 0% | 2.5 GB |
| **One process, no gRPC (`6e6079d`)** | **8,544** | **207 ms** | **240 ms** | **254 ms** | **0%** | **1.9 GB** |
| `examples/go`, for comparison | 8,088 | 168 ms | 551 ms | 2,826 ms | 0% | 1.1 GB |

- The Go example ran without Nginx in front, although its README says it runs behind Nginx. It was measured once, earlier.
- The `6e6079d` row was measured just before the unused gRPC projects were deleted, with the same code path.
- Repeat runs of the same code later gave 7,101, 8,413 and 7,966 requests/s, so differences under about 17% mean little.

`bed32c1` was measured side by side with `6e6079d`, on 2 CPUs each at 2,000 users, during a CUDA build. It went from
1,826 and 1,748 requests/s to 3,417 and 3,000, over two rounds with the CPUs swapped.

## Lessons

- **The stall:** handlers held a connection while dispatching a query that needed a second one from the same pool. Under
  load, requests held every connection while waiting for another, and Articles timed out at low CPU. The unit of work now
  rules this out, because a request only ever uses one connection.
- **gRPC's cost:** removing the hop and the extra processes roughly halved p95 and p99, and saved about 0.6 GB of memory.
- **A full benchmark crashed WSL:** two full database seeds ran at once, because stopping a background task hadn't killed
  its script. The likely cause is memory, next to a 46 GB vLLM container, but that wasn't confirmed. Short runs on the dev
  seed have been fine.
- **One run was discarded:** CUDA builds running in Docker's VM took the CPU and made the load generator flag itself.

## Open items

- Open a pull request when ready, from the fork. CI runs `install.sh` and `test/run.sh`.
- Login still holds a connection during its bcrypt check.
- No test checks that a failed command rolls back earlier writes in the same request.
- There's no full-seed result yet, so no real user-count score.
- The `qwen3.8-27b-vllm-fp8-offload-3gpu` container didn't come back after the WSL crash.
- `.vs/` and the `Properties/` folders Visual Studio added to three other submissions are untracked. Keep them out of
  commits, because PRs can't touch files outside this folder.
