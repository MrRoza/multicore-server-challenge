using Conduit.Users.Responses;
using Conduit.Shared.Auth;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Users.Commands;

public static class UpdateUser
{
    public record Command(long UserId, string Token, string? Email, string? Username, string? Password, string? Bio, string? Image) : ICommand<UserResponse>;

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, UserResponse>
    {
        public async Task<Result<UserResponse>> Handle(Command command)
        {
            var user = command;
            if (user.Email == "" || user.Username == "" || user.Password == "") return Error.Validation("email, username and password can't be empty");
            if (user.Password is not null && Password.IsTooLong(user.Password)) return Error.Validation("password is over 72 bytes");

            var passwordHash = user.Password is null ? null : BCrypt.Net.BCrypt.HashPassword(user.Password, 10);
            await using (var db = await data.OpenConnectionAsync())
            {
                var row = await db.QuerySingleOrDefaultAsync<UserRow>($"""
                    UPDATE users SET email = COALESCE(@Email, email), username = COALESCE(@Username, username),
                                     password_hash = COALESCE(@passwordHash, password_hash),
                                     bio = COALESCE(@Bio, bio), image = COALESCE(@Image, image)
                     WHERE id = @UserId
                    RETURNING {UserRow.Columns}
                    """, new { command.UserId, user.Email, user.Username, passwordHash, user.Bio, user.Image });
                if (row is null) return Error.Unauthorized("this user no longer exists");
                return new UserResponse(row.ToUser(command.Token));
            }
        }
    }
}
