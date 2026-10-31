using Conduit.Users.Abstractions;
using Conduit.Users.Integrations.Protos;

namespace Conduit.Users.Integrations;

public sealed class UsersGrpcClient(UsersRpc.UsersRpcClient rpc) : IUsersClient
{
    public async Task<UserResponseDto> Register(string? username, string? email, string? password) =>
        ToDto(await rpc.RegisterAsync(new RegisterUserMessage { Username = username, Email = email, Password = password }));

    public async Task<UserResponseDto> Login(string? email, string? password) =>
        ToDto(await rpc.LoginAsync(new LoginUserMessage { Email = email, Password = password }));

    public async Task<UserResponseDto> GetCurrent(long userId, string token) =>
        ToDto(await rpc.GetCurrentAsync(new GetCurrentUserMessage { UserId = userId, Token = token }));

    public async Task<UserResponseDto> Update(long userId, string token, string? email, string? username, string? password, string? bio, string? image) =>
        ToDto(await rpc.UpdateAsync(new UpdateUserMessage { UserId = userId, Token = token, Email = email, Username = username, Password = password, Bio = bio, Image = image }));

    static UserResponseDto ToDto(UserMessage user) => new(new UserDto(user.Email, user.Token, user.Username, user.Bio, user.Image));
}
