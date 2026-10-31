namespace Conduit.Shared.Contracts;

public class ProfileRowDto
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string? Bio { get; init; }
    public string? Image { get; init; }
    public bool Following { get; init; }

    public ProfileDto ToProfile() => new(Username, Bio, Image, Following);
}
