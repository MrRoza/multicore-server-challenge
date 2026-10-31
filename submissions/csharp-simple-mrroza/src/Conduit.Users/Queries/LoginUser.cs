using Conduit.Users.Responses;
using Conduit.Shared.Auth;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Users.Queries;

public static class LoginUser
{
    public record Query(string? Email, string? Password) : IQuery<UserResponse>;

    public sealed class Handler(NpgsqlDataSource data, TokenService tokens) : IQueryHandler<Query, UserResponse>
    {
        public async Task<Result<UserResponse>> Handle(Query query)
        {
            if (string.IsNullOrEmpty(query.Email) || string.IsNullOrEmpty(query.Password))
                return Error.Validation("email and password are required");

            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleOrDefaultAsync<UserRow>(
                $"SELECT {UserRow.Columns}, password_hash FROM users WHERE email = @Email", new { query.Email });
            if (row is null || Password.IsTooLong(query.Password) || !BCrypt.Net.BCrypt.Verify(query.Password, row.PasswordHash))
                return Error.Unauthorized("wrong email or password");
            return new UserResponse(row.ToUser(tokens.Create(row.Id)));
        }
    }
}
