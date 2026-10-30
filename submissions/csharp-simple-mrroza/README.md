# C#: ASP.NET Core minimal APIs + Dapper

Front: direct

Conduit written like a regular ASP.NET Core app, to be easy to read rather than fast: minimal API endpoints,
dependency injection, the standard JWT bearer handler, and the reference SQL from [`SPEC.md`](../../SPEC.md) run
through Dapper.

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
| `Program.cs` | Startup |
| `Endpoints/` | Users, profiles, articles, comments, tags |
| `Models/` | Requests, responses and database rows |
| `Data/` | Database setup and the article SQL |
| `Auth/` | JWT setup and issuing tokens |

## Optimizations

None on purpose. The only limit set is a connection pool of 90, to stay under the challenge's limit of 95.
Logging is at Warning level in `appsettings.json`. This hasn't been load tested.
