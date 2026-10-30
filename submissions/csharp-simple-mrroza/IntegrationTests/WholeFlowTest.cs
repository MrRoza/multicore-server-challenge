using System.Net;
using Xunit;

namespace IntegrationTests;

/// <summary>Two people using the app from sign-up to clean-up, each step depending on the ones before.</summary>
[Collection(ConduitCollection.Name)]
public class WholeFlowTest(ConduitApp app)
{
    readonly ApiClient api = app.Client();

    [Fact]
    public async Task Two_users_publish_follow_favorite_and_comment()
    {
        // Sign up and log in again.
        var writer = await api.Register("writer");
        var reader = await api.Register("reader");
        var login = (await api.Post("/api/users/login", new { user = new { email = reader.Email, password = reader.Password } })).Expect(HttpStatusCode.OK);
        var readerToken = login.Json["user"]!["token"]!.GetValue<string>();

        // The writer fills in a profile and publishes.
        (await api.Put("/api/user", new { user = new { bio = "I write" } }, writer.Token)).Expect(HttpStatusCode.OK);
        var tag = Names.Unique("flow");
        var slug = (await api.CreateArticle(writer.Token, "My First Post", [tag]))["slug"]!.GetValue<string>();

        // The reader finds it by tag and sees the writer's new bio.
        var found = (await api.Get($"/api/articles?tag={tag}", readerToken)).Expect(HttpStatusCode.OK).Json["articles"]![0]!;
        Assert.Equal(slug, found["slug"]!.GetValue<string>());
        Assert.Equal("I write", found["author"]!["bio"]!.GetValue<string>());
        Assert.False(found["author"]!["following"]!.GetValue<bool>());

        // Following puts it in the reader's feed.
        (await api.Post($"/api/profiles/{writer.Username}/follow", token: readerToken)).Expect(HttpStatusCode.OK);
        var feed = (await api.Get("/api/articles/feed", readerToken)).Expect(HttpStatusCode.OK).Json["articles"]![0]!;
        Assert.Equal(slug, feed["slug"]!.GetValue<string>());
        Assert.True(feed["author"]!["following"]!.GetValue<bool>());

        // The reader favorites and comments.
        (await api.Post($"/api/articles/{slug}/favorite", token: readerToken)).Expect(HttpStatusCode.OK);
        var comment = (await api.Post($"/api/articles/{slug}/comments", new { comment = new { body = "Nice post" } }, readerToken)).Expect(HttpStatusCode.Created);
        var commentId = comment.Json["comment"]!["id"]!.GetValue<long>();

        // The writer sees both, then renames the article, which keeps them.
        var seen = (await api.Get($"/api/articles/{slug}", writer.Token)).Expect(HttpStatusCode.OK).Json["article"]!;
        Assert.Equal(1, seen["favoritesCount"]!.GetValue<long>());
        var renamed = (await api.Put($"/api/articles/{slug}", new { article = new { title = "Better Title" } }, writer.Token)).Expect(HttpStatusCode.OK).Json["article"]!;
        var newSlug = renamed["slug"]!.GetValue<string>();
        Assert.StartsWith("better-title-", newSlug);
        Assert.Equal(1, renamed["favoritesCount"]!.GetValue<long>());
        Assert.Single((await api.Get($"/api/articles/{newSlug}/comments")).Expect(HttpStatusCode.OK).Json["comments"]!.AsArray());

        // The writer can't remove the reader's comment; the reader can.
        (await api.Delete($"/api/articles/{newSlug}/comments/{commentId}", writer.Token)).ExpectError(HttpStatusCode.Forbidden);
        (await api.Delete($"/api/articles/{newSlug}/comments/{commentId}", readerToken)).Expect(HttpStatusCode.NoContent);

        // Deleting the article removes it from the lists and the feed.
        (await api.Delete($"/api/articles/{newSlug}", writer.Token)).Expect(HttpStatusCode.NoContent);
        (await api.Get($"/api/articles/{newSlug}/comments")).ExpectError(HttpStatusCode.NotFound);
        Assert.Empty((await api.Get($"/api/articles?tag={tag}")).Expect(HttpStatusCode.OK).Json["articles"]!.AsArray());
        Assert.Empty((await api.Get("/api/articles/feed", readerToken)).Expect(HttpStatusCode.OK).Json["articles"]!.AsArray());
    }
}
