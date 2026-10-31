# C#: ASP.NET Core distributed monolith with vertical slices, CQRS and gRPC

Front: direct

Conduit as a distributed monolith, written to be easy to read rather than fast. Each slice is its own gRPC service in
its own project and process, and all of them share one Postgres database. A gateway serves the HTTP API on `HOST:PORT`
and calls the slices over gRPC. The SQL is the reference SQL from [`SPEC.md`](../../SPEC.md), run through Dapper.

| | Version |
|---|---|
| .NET | 10 (SDK 10.0.402) |
| ASP.NET Core | 10, minimal APIs on Kestrel |
| gRPC | Grpc.AspNetCore 2.71.0, Grpc.Net.ClientFactory 2.71.0 |
| Database | Dapper 2.1.66, Npgsql 10.0.3 |
| JWT | Microsoft.AspNetCore.Authentication.JwtBearer 10.0.13 |
| bcrypt | BCrypt.Net-Next 4.2.0, cost 10 |

## Services

| Project | Port | Postgres connections | Does |
|---|---:|---:|---|
| `Conduit.Gateway` | `HOST:PORT` | none | The HTTP API: routes, JSON, JWT checks |
| `Conduit.Users` | 13001 | 10 | Sign-up, login, the current user. Issues tokens |
| `Conduit.Profiles` | 13002 | 10 | Profiles and follows |
| `Conduit.Articles` | 13003 | 45 | Articles, the feed, favorites |
| `Conduit.Comments` | 13004 | 20 | Comments |
| `Conduit.Tags` | 13005 | 5 | Tags |

Only the gateway calls the slices. A slice that needs another's data reads the shared tables. `start.sh` runs all of
them, and if one exits, it stops the rest.

## Code

| Folder | |
|---|---|
| `src/Conduit.Contracts/` | The `.proto` files |
| `src/Conduit.Gateway/` | `Program.cs`, and per slice its `Requests/`, `Responses/` and routes |
| `src/Conduit.<Slice>/` | A service, with `Commands/`, `Queries/` and `Responses/` folders and its gRPC service. Every type has its own file, except that a command or query shares one with its handler |
| `src/Conduit.Shared/` | Service startup, database setup, slice registration, and the profile types in `Contracts/` |
| `src/Conduit.Shared/Auth/` | JWT setup, issuing tokens, password rules |
| `src/Conduit.Shared/Cqrs/` | The dispatcher, command and query interfaces, results |

Each slice has a `ServiceCollectionExtensions` with an `Add<Slice>` method, such as `AddUsers`, that registers its
handlers and its gRPC service. The gateway's `Add<Slice>Api` methods do the same for its gRPC client and HTTP routes.
`app.MapSlices()` maps them once the app is built, because routes can't be mapped on an `IServiceCollection`.

Commands change state and queries only read. A gRPC service builds a command or query, gives it to `IDispatcher` and
returns the response. The dispatcher, written here in `Conduit.Shared/Cqrs/Dispatcher.cs`, finds the handler through
dependency injection. A command that responds with current data, such as an article, dispatches a query for it after
its write. Errors travel as gRPC statuses, which the gateway maps to HTTP.

## Tests

`run.sh` runs what CI runs, in Docker. `IntegrationTests/` starts every service as its own process and sends real HTTP requests to the gateway, with
Postgres 18 in a container. It needs Docker.

```bash
dotnet test IntegrationTests
```

## Optimizations

None on purpose: the extra processes and the gRPC hop cost performance. Connection pools add up to 90, to stay under
the challenge's limit of 95. Logging is at Warning level in each `appsettings.json`. This hasn't been load tested.
