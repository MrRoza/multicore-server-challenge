namespace Conduit.Gateway.Users.Responses;

public record User(string Email, string Token, string Username, string? Bio, string? Image);
