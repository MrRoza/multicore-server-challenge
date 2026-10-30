using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Users;

public sealed class RegisterUser : IEndpoint
{
    public record Command(string? Username, string? Email, string? Password);

    public record Request(Command? User);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/users", async (Request request, ICommandHandler<Command, User> handler) =>
            (await handler.Handle(request.User ?? new Command(null, null, null)))
                .ToHttp(user => Results.Json(new UserResponse(user), statusCode: StatusCodes.Status201Created)));

    public sealed class Handler(NpgsqlDataSource data, TokenService tokens) : ICommandHandler<Command, User>
    {
        public async Task<Result<User>> Handle(Command command)
        {
            if (string.IsNullOrEmpty(command.Username) || string.IsNullOrEmpty(command.Email) || string.IsNullOrEmpty(command.Password))
                return Error.Validation("username, email and password are required");
            if (Password.IsTooLong(command.Password)) return Error.Validation("password is over 72 bytes");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(command.Password, 10);
            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleAsync<UserRow>(
                $"INSERT INTO users (username, email, password_hash) VALUES (@Username, @Email, @passwordHash) RETURNING {UserRow.Columns}",
                new { command.Username, command.Email, passwordHash });
            return row.ToUser(tokens.Create(row.Id));
        }
    }
}
