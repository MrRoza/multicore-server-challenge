using System.Net;
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
