# C#: ASP.NET Core distributed monolith with vertical slices, CQRS and gRPC

Front: direct

Conduit as a distributed monolith, written to be easy to read rather than fast. Each slice is its own gRPC service and
process, split into layered projects, and all of them share one Postgres database. A gateway serves the HTTP API on
`HOST:PORT` and calls the slices over gRPC. The SQL is the reference SQL from [`SPEC.md`](../../SPEC.md), run through
Dapper.

| | Version |
|---|---|
| .NET | 10 (SDK 10.0.402) |
| ASP.NET Core | 10, minimal APIs on Kestrel |
| gRPC | Grpc.AspNetCore 2.71.0, Grpc.Net.ClientFactory 2.71.0 |
| Database | Dapper 2.1.66, Npgsql 10.0.3 |
| JWT | Microsoft.AspNetCore.Authentication.JwtBearer 10.0.13 |
| bcrypt | BCrypt.Net-Next 4.2.0, cost 10 |

## Processes

| Project | Port | Postgres connections | Does |
|---|---:|---:|---|
| `Conduit.Gateway` | `HOST:PORT` | none | The HTTP API: routes, JSON, JWT checks |
| `Conduit.Users.Api` | 13001 | 10 | Sign-up, login, the current user. Issues tokens |
| `Conduit.Profiles.Api` | 13002 | 10 | Profiles and follows |
| `Conduit.Articles.Api` | 13003 | 45 | Articles, the feed, favorites |
| `Conduit.Comments.Api` | 13004 | 20 | Comments |
| `Conduit.Tags.Api` | 13005 | 5 | Tags |

Only the gateway calls the slices. A slice that needs another's data reads the shared tables. `start.sh` runs all of
them, and if one exits, it stops the rest.

## Projects

Each slice in `src/<Slice>/` has the same five projects:

| Project | Holds | Depends on |
|---|---|---|
| `Conduit.<Slice>.Abstractions` | DTOs, repository interfaces, value objects | `Conduit.Shared` |
| `Conduit.<Slice>` | Commands, queries and their handlers | Abstractions |
| `Conduit.<Slice>.Data.Postgres` | Repositories: the SQL and Dapper | Abstractions |
| `Conduit.<Slice>.Integrations` | The `.proto` file and the client other processes use | Abstractions |
| `Conduit.<Slice>.Api` | The process: `Program.cs` and the gRPC service | All of the above |

Next to them, `src/Conduit.Gateway/` holds the HTTP routes and request DTOs. `src/Conduit.Shared/` holds service startup,
database setup, slice registration, the profile types in `Contracts/`, auth in `Auth/` and the dispatcher in `Cqrs/`.

Every type has its own file, except that a command or query shares one with its handler.

Commands change state and queries only read. Each gRPC method opens an `IUnitOfWork`, gives a command or query to
`IDispatcher` and returns the response. The unit of work holds one connection for the whole request, and repositories
use it. Commands run in a transaction that commits at the end and rolls back on any error. Queries have no transaction.
Errors travel as gRPC statuses, which the gateway maps to HTTP.

## Tests

`run.sh` runs what CI runs, in Docker. `IntegrationTests/` starts every process and sends real HTTP requests to the
gateway, with Postgres 18 in a container. It needs Docker.

```bash
bash run.sh
dotnet test IntegrationTests
```

## Optimizations

None on purpose: the extra processes and the gRPC hop cost performance. Connection pools add up to 90, to stay under
the challenge's limit of 95. Logging is at Warning level in each `appsettings.json`. This hasn't been load tested.
