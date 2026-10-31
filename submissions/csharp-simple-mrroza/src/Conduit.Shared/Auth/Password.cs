using System.Text;

namespace Conduit.Shared.Auth;

public static class Password
{
    /// <summary>bcrypt only uses the first 72 bytes, so a longer password is refused instead of silently cut.</summary>
    public static bool IsTooLong(string password) => Encoding.UTF8.GetByteCount(password) > 72;
}
