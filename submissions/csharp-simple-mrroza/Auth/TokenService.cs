using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Conduit.Auth;

public class TokenService(SecurityKey key)
{
    readonly SigningCredentials credentials = new(key, SecurityAlgorithms.HmacSha256);
    readonly JsonWebTokenHandler handler = new() { SetDefaultTimesOnTokenCreation = false };

    public string Create(long userId)
    {
        var now = DateTime.UtcNow;
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object> { [JwtRegisteredClaimNames.Sub] = userId.ToString() },
            IssuedAt = now,
            Expires = now.AddDays(30),
            SigningCredentials = credentials,
        });
    }
}
