using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Users;

public sealed class GetCurrentUser : IEndpoint
{
    public record Query(long UserId, string Token);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/user", async (ClaimsPrincipal principal, HttpRequest http, IQueryHandler<Query, User> handler) =>
            (await handler.Handle(new Query(principal.UserId(), TokenAuth.PresentedToken(http)!)))
                .ToHttp(user => Results.Ok(new UserResponse(user))))
            .AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, User>
    {
        public async Task<Result<User>> Handle(Query query)
        {
            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleOrDefaultAsync<UserRow>(
                $"SELECT {UserRow.Columns} FROM users WHERE id = @UserId", new { query.UserId });
            if (row is null) return Error.Unauthorized("this user no longer exists");
            return row.ToUser(query.Token);
        }
    }
}
