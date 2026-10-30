# C#: ASP.NET Core minimal APIs with feature slices and CQRS

Front: direct

Conduit organised the way many ASP.NET Core apps are, to be easy to read rather than fast: feature slices, commands
and queries with their own handlers, a small dispatcher, the standard JWT bearer handler, and the reference SQL from
[`SPEC.md`](../../SPEC.md) run through Dapper.

| | Version |
|---|---|
| .NET | 10 (SDK 10.0.402) |
| ASP.NET Core | 10, minimal APIs on Kestrel |
| Database | Dapper 2.1.66, Npgsql 10.0.3 |
| JWT | Microsoft.AspNetCore.Authentication.JwtBearer 10.0.13 |
| bcrypt | BCrypt.Net-Next 4.2.0, cost 10 |

## Code

| Folder | |
|---|---|
| `Features/` | One folder per slice, with `Commands/`, `Queries/`, `Requests/` and `Responses/` folders. Every type has its own file, except that a command or query shares one with its handler |
| `Shared/` | Database setup, error handling, slice registration |
| `Shared/Auth/` | JWT setup, issuing tokens, password rules |
| `Shared/Cqrs/` | The dispatcher, command and query interfaces, results |
| `Program.cs` | Startup |

Each slice has a `ServiceCollectionExtensions` with an `Add<Slice>` method, such as `AddUsers`, that registers its
handlers and its routes. `app.MapSlices()` maps those routes once the app is built, because routes can't be mapped on
an `IServiceCollection`.

Commands change state and queries only read. An endpoint builds a command or query, gives it to `IDispatcher` and
returns the response. The dispatcher, written here in `Shared/Cqrs/Dispatcher.cs`, finds the handler through dependency
injection. A command that responds with current data, such as an article, dispatches a query for it after its write.

## Tests

`IntegrationTests/` starts the app on Kestrel and sends real HTTP requests to it over a socket, with Postgres 18 in a
container. It needs Docker.

```bash
dotnet test IntegrationTests
```

## Optimizations

None on purpose. The only limit set is a connection pool of 90, to stay under the challenge's limit of 95.
Logging is at Warning level in `appsettings.json`. This hasn't been load tested.
