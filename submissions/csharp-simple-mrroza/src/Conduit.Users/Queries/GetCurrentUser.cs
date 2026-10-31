using Conduit.Users.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Users.Queries;

public static class GetCurrentUser
{
    public record Query(long UserId, string Token) : IQuery<UserResponseDto>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, UserResponseDto>
    {
        public async Task<Result<UserResponseDto>> Handle(Query query)
        {
            await using (var db = await data.OpenConnectionAsync())
            {
                var row = await db.QuerySingleOrDefaultAsync<UserRowDto>(
                    $"SELECT {UserRowDto.Columns} FROM users WHERE id = @UserId", new { query.UserId });
                if (row is null) return Error.Unauthorized("this user no longer exists");
                return new UserResponseDto(row.ToUser(query.Token));
            }
        }
    }
}
