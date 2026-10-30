using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntegrationTests;

/// <summary>Signs tokens the server didn't issue, to test what it accepts.</summary>
public static class Tokens
{
    public static string Sign(object claims, string secret = ConduitApp.JwtSecret, string alg = "HS256")
    {
        var signed = Encode(new { alg, typ = "JWT" }) + "." + Encode(claims);
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(signed));
        return signed + "." + Base64Url.EncodeToString(signature);
    }

    public static string Unsigned(object claims) => Encode(new { alg = "none", typ = "JWT" }) + "." + Encode(claims) + ".";

    public static JsonNode Claims(string token) => JsonNode.Parse(Base64Url.DecodeFromChars(token.Split('.')[1]))!;

    public static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    static string Encode(object value) => Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(value));
}
