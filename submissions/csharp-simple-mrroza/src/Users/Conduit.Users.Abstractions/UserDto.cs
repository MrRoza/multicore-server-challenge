namespace Conduit.Users.Abstractions;

public record UserDto(string Email, string Token, string Username, string? Bio, string? Image);
