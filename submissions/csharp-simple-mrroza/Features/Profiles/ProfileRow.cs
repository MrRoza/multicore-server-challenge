namespace Conduit.Features.Profiles;

public class ProfileRow
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string? Bio { get; init; }
    public string? Image { get; init; }
    public bool Following { get; init; }

    public Profile ToProfile() => new(Username, Bio, Image, Following);
}
