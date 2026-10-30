using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace IntegrationTests;

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
}
