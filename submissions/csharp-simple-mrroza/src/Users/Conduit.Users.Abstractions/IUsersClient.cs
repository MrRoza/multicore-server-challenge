namespace Conduit.Users.Abstractions;

/// <summary>How other parts of the app use the Users slice. Failures arrive as exceptions.</summary>
public interface IUsersClient
{
    Task<UserResponseDto> Register(string? username, string? email, string? password);
    Task<UserResponseDto> Login(string? email, string? password);
    Task<UserResponseDto> GetCurrent(long userId, string token);
    Task<UserResponseDto> Update(long userId, string token, string? email, string? username, string? password, string? bio, string? image);
}
