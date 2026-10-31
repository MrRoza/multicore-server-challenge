using Conduit.Contracts;

namespace Conduit.Gateway.Users.Responses;

public record UserResponseDto(UserDto User)
{
    public static UserResponseDto From(UserMessage user) =>
        new(new UserDto(user.Email, user.Token, user.Username, user.Bio, user.Image));
}
