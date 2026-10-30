# C#: ASP.NET Core minimal APIs with feature slices and CQRS

Front: direct

Conduit organised the way many ASP.NET Core apps are, to be easy to read rather than fast: one file per use case,
commands and queries with their own handlers, the standard JWT bearer handler, and the reference SQL from
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
| `Features/` | One folder per area, one file per use case: its route, command or query, and handler |
| `Shared/` | Handler interfaces, results, auth, database setup, error handling |
| `Program.cs` | Startup. Handlers and endpoints are found by reflection |

Commands change state and queries only read. A command returns only what it created, so an endpoint that responds
with an article runs its command and then the `GetArticle` query. There is no mediator: endpoints inject the handlers
they use.

## Optimizations

None on purpose. The only limit set is a connection pool of 90, to stay under the challenge's limit of 95.
Logging is at Warning level in `appsettings.json`. This hasn't been load tested.
