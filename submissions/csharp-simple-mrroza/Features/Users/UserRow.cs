namespace Conduit.Features.Users;

public class UserRow
{
    public const string Columns = "id, username, email, bio, image";

    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Bio { get; init; }
    public string? Image { get; init; }
    public string PasswordHash { get; init; } = "";

    public User ToUser(string token) => new(Email, token, Username, Bio, Image);
}
