using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Users;

public sealed class UpdateUser : IEndpoint
{
    public record Fields(string? Email, string? Username, string? Password, string? Bio, string? Image);

    public record Request(Fields? User);

    public record Command(long UserId, string Token, Fields? Changes);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapPut("/user", async (Request request, ClaimsPrincipal principal, HttpRequest http, ICommandHandler<Command, User> handler) =>
            (await handler.Handle(new Command(principal.UserId(), TokenAuth.PresentedToken(http)!, request.User)))
                .ToHttp(user => Results.Ok(new UserResponse(user))))
            .AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, User>
    {
        public async Task<Result<User>> Handle(Command command)
        {
            if (command.Changes is not { } user) return Error.Validation("user is required");
            if (user.Email == "" || user.Username == "" || user.Password == "") return Error.Validation("email, username and password can't be empty");
            if (user.Password is not null && Password.IsTooLong(user.Password)) return Error.Validation("password is over 72 bytes");

            var passwordHash = user.Password is null ? null : BCrypt.Net.BCrypt.HashPassword(user.Password, 10);
            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleOrDefaultAsync<UserRow>($"""
                UPDATE users SET email = COALESCE(@Email, email), username = COALESCE(@Username, username),
                                 password_hash = COALESCE(@passwordHash, password_hash),
                                 bio = COALESCE(@Bio, bio), image = COALESCE(@Image, image)
                 WHERE id = @UserId
                RETURNING {UserRow.Columns}
                """, new { command.UserId, user.Email, user.Username, passwordHash, user.Bio, user.Image });
            if (row is null) return Error.Unauthorized("this user no longer exists");
            return row.ToUser(command.Token);
        }
    }
}
