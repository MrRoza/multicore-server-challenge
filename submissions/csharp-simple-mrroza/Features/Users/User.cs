using System.Text;

namespace Conduit.Features.Users;

public record User(string Email, string Token, string Username, string? Bio, string? Image);

public record UserResponse(User User);

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

public static class Password
{
    /// <summary>bcrypt only uses the first 72 bytes, so a longer password is refused instead of silently cut.</summary>
    public static bool IsTooLong(string password) => Encoding.UTF8.GetByteCount(password) > 72;
}
