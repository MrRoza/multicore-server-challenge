using Conduit.Shared.Auth;
using Conduit.Shared.Cqrs;
using Conduit.Users.Abstractions;

namespace Conduit.Users.Commands;

public static class UpdateUser
{
    public record Command(long UserId, string Token, string? Email, string? Username, string? Password, string? Bio, string? Image) : ICommand<UserResponseDto>;

    public sealed class Handler(IUserRepository users) : ICommandHandler<Command, UserResponseDto>
    {
        public async Task<Result<UserResponseDto>> Handle(Command command)
        {
            if (command.Email == "" || command.Username == "" || command.Password == "") return Error.Validation("email, username and password can't be empty");
            if (command.Password is not null && Password.IsTooLong(command.Password)) return Error.Validation("password is over 72 bytes");

            var passwordHash = command.Password is null ? null : BCrypt.Net.BCrypt.HashPassword(command.Password, 10);
            var row = await users.Update(command.UserId, command.Email, command.Username, passwordHash, command.Bio, command.Image);
            if (row is null) return Error.Unauthorized("this user no longer exists");
            return new UserResponseDto(row.ToUser(command.Token));
        }
    }
}
