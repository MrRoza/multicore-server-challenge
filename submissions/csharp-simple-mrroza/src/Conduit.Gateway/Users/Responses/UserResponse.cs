using Conduit.Contracts;

namespace Conduit.Gateway.Users.Responses;

public record UserResponse(User User)
{
    public static UserResponse From(UserMessage user) =>
        new(new User(user.Email, user.Token, user.Username, user.Bio, user.Image));
}
