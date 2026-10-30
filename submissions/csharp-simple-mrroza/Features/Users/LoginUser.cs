using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Users;

public sealed class LoginUser : IEndpoint
{
    public record Query(string? Email, string? Password);

    public record Request(Query? User);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/users/login", async (Request request, IQueryHandler<Query, User> handler) =>
            (await handler.Handle(request.User ?? new Query(null, null)))
                .ToHttp(user => Results.Ok(new UserResponse(user))));

    public sealed class Handler(NpgsqlDataSource data, TokenService tokens) : IQueryHandler<Query, User>
    {
        public async Task<Result<User>> Handle(Query query)
        {
            if (string.IsNullOrEmpty(query.Email) || string.IsNullOrEmpty(query.Password))
                return Error.Validation("email and password are required");

            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleOrDefaultAsync<UserRow>(
                $"SELECT {UserRow.Columns}, password_hash FROM users WHERE email = @Email", new { query.Email });
            if (row is null || Password.IsTooLong(query.Password) || !BCrypt.Net.BCrypt.Verify(query.Password, row.PasswordHash))
                return Error.Unauthorized("wrong email or password");
            return row.ToUser(tokens.Create(row.Id));
        }
    }
}
