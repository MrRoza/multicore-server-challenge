namespace Conduit.Gateway.Users.Requests;

public record UpdateUserFields(string? Email, string? Username, string? Password, string? Bio, string? Image);
