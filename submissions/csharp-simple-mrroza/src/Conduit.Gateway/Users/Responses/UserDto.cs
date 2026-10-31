namespace Conduit.Gateway.Users.Responses;

public record UserDto(string Email, string Token, string Username, string? Bio, string? Image);
