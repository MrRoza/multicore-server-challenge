using System.Buffers.Text;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit;

namespace IntegrationTests;

public record ApiResponse(HttpStatusCode Status, string? ContentType, string Text)
{
    public JsonNode Json => JsonNode.Parse(Text)!;

    public ApiResponse Expect(HttpStatusCode status)
    {
        Assert.True(Status == status, $"status {(int)Status}, want {(int)status}. Body: {Text}");
        if (status == HttpStatusCode.NoContent)
        {
            Assert.Empty(Text);
        }
        else
        {
            Assert.Equal("application/json", ContentType);
        }
        return this;
    }

    /// <summary>Checks the status and the {"errors": {"body": ["message"]}} shape.</summary>
    public void ExpectError(HttpStatusCode status)
    {
        Expect(status);
        var messages = Json["errors"]!["body"]!.AsArray();
        Assert.NotEmpty(messages);
        Assert.All(messages, message => Assert.False(string.IsNullOrEmpty(message!.GetValue<string>())));
    }
}

public record TestUser(string Username, string Email, string Password, string Token);

public sealed class ApiClient(HttpClient http)
{
    public Task<ApiResponse> Get(string path, string? token = null) => Send(HttpMethod.Get, path, token);
    public Task<ApiResponse> Delete(string path, string? token = null) => Send(HttpMethod.Delete, path, token);
    public Task<ApiResponse> Post(string path, object? body = null, string? token = null) => Send(HttpMethod.Post, path, token, body);
    public Task<ApiResponse> Put(string path, object? body = null, string? token = null) => Send(HttpMethod.Put, path, token, body);

    /// <summary>Sends a body as is, for malformed JSON.</summary>
    public Task<ApiResponse> PostRaw(string path, string body, string? token = null) => Send(HttpMethod.Post, path, token, new RawBody(body));

    /// <summary>Sends this exact Authorization header, for the wrong schemes.</summary>
    public async Task<ApiResponse> GetWithHeader(string path, string authorization)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.TryAddWithoutValidation("Authorization", authorization);
        return await Read(await http.SendAsync(request));
    }

    public async Task<TestUser> Register(string? prefix = null)
    {
        var name = Names.Unique(prefix ?? "user");
        var (email, password) = ($"{name}@example.com", "password-1");
        var response = await Post("/api/users", new { user = new { username = name, email, password } });
        response.Expect(HttpStatusCode.Created);
        return new TestUser(name, email, password, response.Json["user"]!["token"]!.GetValue<string>());
    }

    public async Task<JsonNode> CreateArticle(string token, string? title = null, string[]? tags = null)
    {
        var article = new { title = title ?? Names.Unique("Title"), description = "A description", body = "A body", tagList = tags ?? [] };
        var response = await Post("/api/articles", new { article }, token);
        response.Expect(HttpStatusCode.Created);
        return response.Json["article"]!;
    }

    async Task<ApiResponse> Send(HttpMethod method, string path, string? token, object? body = null)
    {
        using var request = new HttpRequestMessage(method, path);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Token", token);
        if (body is RawBody raw) request.Content = new StringContent(raw.Text, Encoding.UTF8, "application/json");
        else if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return await Read(await http.SendAsync(request));
    }

    static async Task<ApiResponse> Read(HttpResponseMessage response)
    {
        using (response)
        {
            var text = await response.Content.ReadAsStringAsync();
            return new ApiResponse(response.StatusCode, response.Content.Headers.ContentType?.MediaType, text);
        }
    }

    record RawBody(string Text);
}

public static class Names
{
    public static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}";
}

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
