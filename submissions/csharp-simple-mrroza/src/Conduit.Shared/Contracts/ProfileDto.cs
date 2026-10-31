namespace Conduit.Shared.Contracts;

public record ProfileDto(string Username, string? Bio, string? Image, bool Following);
