using Conduit.Shared.Auth;
using Conduit.Shared.Cqrs;
using Conduit.Users.Abstractions;

namespace Conduit.Users.Commands;

public static class RegisterUser
{
    public record Command(string? Username, string? Email, string? Password) : ICommand<UserResponseDto>;

    public sealed class Handler(IUserRepository users, TokenService tokens) : ICommandHandler<Command, UserResponseDto>
    {
        public async Task<Result<UserResponseDto>> Handle(Command command)
        {
            if (string.IsNullOrEmpty(command.Username) || string.IsNullOrEmpty(command.Email) || string.IsNullOrEmpty(command.Password))
                return Error.Validation("username, email and password are required");
            if (Password.IsTooLong(command.Password)) return Error.Validation("password is over 72 bytes");

            var row = await users.Create(command.Username, command.Email, BCrypt.Net.BCrypt.HashPassword(command.Password, 10));
            return new UserResponseDto(row.ToUser(tokens.Create(row.Id)));
        }
    }
}
