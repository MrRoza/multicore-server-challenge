namespace Conduit.Users;

public record UserDto(string Email, string Token, string Username, string? Bio, string? Image);
