using System.Buffers.Text;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Conduit.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Conduit.Auth;

public static class TokenAuth
{
    const string Scheme = "Token ";
    const string OptionalPolicy = "optional";

    public static IServiceCollection AddTokenAuth(this IServiceCollection services, IConfiguration configuration)
    {
        var secret = configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is required");
        var key = new SymmetricSecurityKey(SigningKey(secret));
        services.AddSingleton(new TokenService(key));

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                IssuerSigningKey = key,
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                ValidateIssuer = false,
                ValidateAudience = false,
                ClockSkew = TimeSpan.Zero,
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    context.Token = PresentedToken(context.Request);
                    if (context.Token is null) context.NoResult();
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    if (!HasUserIdSubject((JsonWebToken)context.SecurityToken)) context.Fail("sub is not a user id");
                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    context.HandleResponse();
                    return ApiErrors.Unauthorized("sign in first").ExecuteAsync(context.HttpContext);
                },
            };
        });

        // Optional auth: no header means anonymous, but a header that is sent must hold a valid token.
        services.AddAuthorizationBuilder().AddPolicy(OptionalPolicy, policy => policy.RequireAssertion(context =>
            context.User.Identity?.IsAuthenticated == true ||
            context.Resource is HttpContext http && !http.Request.Headers.ContainsKey("Authorization")));

        return services;
    }

    public static RouteHandlerBuilder AuthRequired(this RouteHandlerBuilder route) => route.RequireAuthorization();

    public static RouteHandlerBuilder AuthOptional(this RouteHandlerBuilder route) => route.RequireAuthorization(OptionalPolicy);

    /// <summary>The signed-in user's id, or 0 when anonymous.</summary>
    public static long UserId(this ClaimsPrincipal user) =>
        long.TryParse(user.FindFirstValue(JwtRegisteredClaimNames.Sub), out var id) ? id : 0;

    public static string? PresentedToken(HttpRequest request)
    {
        var header = request.Headers.Authorization.ToString();
        return header.StartsWith(Scheme, StringComparison.Ordinal) ? header[Scheme.Length..] : null;
    }

    /// <summary>The spec's sub is a decimal string. IdentityModel turns a JSON number into the same claim, so check the JSON.</summary>
    static bool HasUserIdSubject(JsonWebToken token)
    {
        using var payload = JsonDocument.Parse(Base64Url.DecodeFromChars(token.EncodedPayload));
        return payload.RootElement.TryGetProperty("sub", out var sub)
            && sub.ValueKind == JsonValueKind.String
            && long.TryParse(sub.GetString(), NumberStyles.None, CultureInfo.InvariantCulture, out var id)
            && id > 0;
    }

    /// <summary>
    /// IdentityModel refuses HS256 keys under 32 bytes, and the challenge's secret is 26. HMAC pads a short key with
    /// zeros anyway, so padding it here gives exactly the same signatures.
    /// </summary>
    static byte[] SigningKey(string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);
        if (bytes.Length < 32) Array.Resize(ref bytes, 32);
        return bytes;
    }
}
