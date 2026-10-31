# C#: ASP.NET Core modular monolith with vertical slices and CQRS

Front: direct

Conduit written to be easy to read rather than fast: one process, with each slice split into layered projects, and
commands and queries with their own handlers. The SQL is the reference SQL from [`SPEC.md`](../../SPEC.md), run through
Dapper.

| | Version |
|---|---|
| .NET | 10 (SDK 10.0.402) |
| ASP.NET Core | 10, minimal APIs on Kestrel |
| Database | Dapper 2.1.66, Npgsql 10.0.3 |
| JWT | Microsoft.AspNetCore.Authentication.JwtBearer 10.0.13 |
| bcrypt | BCrypt.Net-Next 4.2.0, cost 10 |

## Projects

`src/Conduit.Host/` is the process that runs, with a connection pool of 90 to stay under the challenge's limit of 95.
It uses these, and calls the slices in memory:

| Project | Holds |
|---|---|
| `src/Conduit.Http/` | The HTTP API: routes, request DTOs, error responses |
| `src/<Slice>/Conduit.<Slice>.Abstractions/` | DTOs, the client interface, repository interfaces, value objects |
| `src/<Slice>/Conduit.<Slice>/` | Commands, queries, their handlers, and the in-process client |
| `src/<Slice>/Conduit.<Slice>.Data.Postgres/` | Repositories: the SQL |
| `src/Conduit.Shared/` | Slice registration, the profile types in `Contracts/`, auth in `Auth/`, the dispatcher in `Cqrs/` |
| `src/Conduit.Shared.Data.Postgres/` | The connection pool, the unit of work, the base repository |

Every type has its own file, except that a command or query shares one with its handler.

Commands change state and queries only read. A route calls a slice's client, such as `IArticlesClient`. The client
begins an `IUnitOfWork`, gives a command or query to `IDispatcher` and returns the response. The unit of work takes one
connection from the pool on the first query and keeps it for the rest of the request. Commands run in a transaction
that commits at the end and rolls back on any error. Queries have no transaction.

## Tests

`run.sh` runs what CI runs, in Docker. `IntegrationTests/` starts the server as a process and sends real HTTP requests
to it, with Postgres 18 in a container. It needs Docker.

```bash
bash run.sh
dotnet test IntegrationTests
```

## Optimizations

Only in how the server talks to the database, so the architecture stays as it is:

- Statements are prepared automatically, and connections aren't reset when they return to the pool.
- A list and its count go in one round trip, and so do a comment list and the check that its article exists.
- The global article list picks its page first, so the subqueries don't run for the rows `OFFSET` skips.

Logging is at Warning level in `appsettings.json`. This hasn't been load tested on an 8-core box.
