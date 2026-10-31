namespace Conduit.Users.Abstractions;

public class UserRowDto
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Bio { get; init; }
    public string? Image { get; init; }
    public string PasswordHash { get; init; } = "";

    public UserDto ToUser(string token) => new(Email, token, Username, Bio, Image);
}
