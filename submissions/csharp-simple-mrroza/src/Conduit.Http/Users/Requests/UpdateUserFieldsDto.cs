namespace Conduit.Http.Users.Requests;

public record UpdateUserFieldsDto(string? Email, string? Username, string? Password, string? Bio, string? Image);
