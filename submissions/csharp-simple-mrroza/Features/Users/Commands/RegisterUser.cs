using Conduit.Features.Users.Responses;
using Conduit.Shared.Auth;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Features.Users.Commands;

public static class RegisterUser
{
    public record Command(string? Username, string? Email, string? Password) : ICommand<UserResponse>;

    public sealed class Handler(NpgsqlDataSource data, TokenService tokens) : ICommandHandler<Command, UserResponse>
    {
        public async Task<Result<UserResponse>> Handle(Command command)
        {
            if (string.IsNullOrEmpty(command.Username) || string.IsNullOrEmpty(command.Email) || string.IsNullOrEmpty(command.Password))
                return Error.Validation("username, email and password are required");
            if (Password.IsTooLong(command.Password)) return Error.Validation("password is over 72 bytes");

            var passwordHash = BCrypt.Net.BCrypt.HashPassword(command.Password, 10);
            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleAsync<UserRow>(
                $"INSERT INTO users (username, email, password_hash) VALUES (@Username, @Email, @passwordHash) RETURNING {UserRow.Columns}",
                new { command.Username, command.Email, passwordHash });
            return new UserResponse(row.ToUser(tokens.Create(row.Id)));
        }
    }
}
