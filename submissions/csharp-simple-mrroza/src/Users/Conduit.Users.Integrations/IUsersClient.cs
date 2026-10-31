using Conduit.Users.Abstractions;

namespace Conduit.Users.Integrations;

/// <summary>Talks to the Users service. Its failures arrive as RpcException.</summary>
public interface IUsersClient
{
    Task<UserResponseDto> Register(string? username, string? email, string? password);
    Task<UserResponseDto> Login(string? email, string? password);
    Task<UserResponseDto> GetCurrent(long userId, string token);
    Task<UserResponseDto> Update(long userId, string token, string? email, string? username, string? password, string? bio, string? image);
}
