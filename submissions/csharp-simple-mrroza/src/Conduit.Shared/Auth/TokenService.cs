using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Conduit.Shared.Auth;

public class TokenService(IConfiguration configuration)
{
    readonly SigningCredentials credentials = new(
        new SymmetricSecurityKey(TokenAuth.SigningKey(configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is required"))),
        SecurityAlgorithms.HmacSha256);
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
