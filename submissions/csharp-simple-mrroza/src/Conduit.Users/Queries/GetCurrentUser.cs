using Conduit.Users.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Users.Queries;

public static class GetCurrentUser
{
    public record Query(long UserId, string Token) : IQuery<UserResponse>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, UserResponse>
    {
        public async Task<Result<UserResponse>> Handle(Query query)
        {
            await using (var db = await data.OpenConnectionAsync())
            {
                var row = await db.QuerySingleOrDefaultAsync<UserRow>(
                    $"SELECT {UserRow.Columns} FROM users WHERE id = @UserId", new { query.UserId });
                if (row is null) return Error.Unauthorized("this user no longer exists");
                return new UserResponse(row.ToUser(query.Token));
            }
        }
    }
}
