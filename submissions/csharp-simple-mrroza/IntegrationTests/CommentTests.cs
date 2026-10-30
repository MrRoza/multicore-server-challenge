using System.Net;
using Xunit;

namespace IntegrationTests;

[Collection(ConduitCollection.Name)]
public class CommentTests(ConduitApp app)
{
    readonly ApiClient api = app.Client();

    async Task<string> NewArticle(TestUser author) => (await api.CreateArticle(author.Token))["slug"]!.GetValue<string>();

    async Task<long> AddComment(string slug, TestUser user, string body = "A comment")
    {
        var response = await api.Post($"/api/articles/{slug}/comments", new { comment = new { body } }, user.Token);
        return response.Expect(HttpStatusCode.Created).Json["comment"]!["id"]!.GetValue<long>();
    }

    [Fact]
    public async Task Create_returns_the_comment()
    {
        var user = await api.Register();
        var slug = await NewArticle(user);
        const string text = "A <b>&</b> \"quoted\" ünïcode comment";

        var comment = (await api.Post($"/api/articles/{slug}/comments", new { comment = new { body = text } }, user.Token))
            .Expect(HttpStatusCode.Created).Json["comment"]!;

        Assert.Equal(["author", "body", "createdAt", "id", "updatedAt"], comment.AsObject().Select(field => field.Key).Order());
        Assert.Equal(text, comment["body"]!.GetValue<string>());
        Assert.Equal(comment["createdAt"]!.GetValue<string>(), comment["updatedAt"]!.GetValue<string>());
        Assert.Equal(user.Username, comment["author"]!["username"]!.GetValue<string>());
        Assert.False(comment["author"]!["following"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Create_validates_before_looking_up_the_article()
    {
        var user = await api.Register();
        var slug = await NewArticle(user);

        (await api.Post($"/api/articles/{slug}/comments", new { comment = new { body = "x" } })).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Post($"/api/articles/{slug}/comments", new { comment = new { body = "" } }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Post($"/api/articles/{slug}/comments", new { comment = new { } }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Post($"/api/articles/{slug}/comments", new { body = "x" }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Post("/api/articles/no-such-article/comments", new { comment = new { body = "x" } }, user.Token)).ExpectError(HttpStatusCode.NotFound);
        (await api.Post("/api/articles/no-such-article/comments", new { comment = new { body = "" } }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task List_is_newest_first_and_shows_following()
    {
        var author = await api.Register();
        var reader = await api.Register();
        var slug = await NewArticle(author);
        var first = await AddComment(slug, author);
        var second = await AddComment(slug, author);
        await api.Post($"/api/profiles/{author.Username}/follow", token: reader.Token);

        var comments = (await api.Get($"/api/articles/{slug}/comments", reader.Token)).Expect(HttpStatusCode.OK).Json["comments"]!.AsArray();

        Assert.Equal([second, first], comments.Select(comment => comment!["id"]!.GetValue<long>()));
        Assert.All(comments, comment => Assert.True(comment!["author"]!["following"]!.GetValue<bool>()));
        (await api.Get("/api/articles/no-such-article/comments")).ExpectError(HttpStatusCode.NotFound);
        (await api.Get($"/api/articles/{slug}/comments", "garbage")).ExpectError(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_stops_at_the_newest_100()
    {
        var user = await api.Register();
        var slug = await NewArticle(user);
        for (var i = 0; i < 101; i++) await AddComment(slug, user);

        Assert.Equal(100, (await api.Get($"/api/articles/{slug}/comments")).Expect(HttpStatusCode.OK).Json["comments"]!.AsArray().Count);
    }

    [Fact]
    public async Task Delete_checks_the_author_article_and_id()
    {
        var author = await api.Register();
        var other = await api.Register();
        var slug = await NewArticle(author);
        var otherSlug = await NewArticle(author);
        var id = await AddComment(slug, author);
        var path = $"/api/articles/{slug}/comments/{id}";

        (await api.Delete(path)).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Delete(path, other.Token)).ExpectError(HttpStatusCode.Forbidden);
        (await api.Delete($"/api/articles/{otherSlug}/comments/{id}", author.Token)).ExpectError(HttpStatusCode.NotFound);
        (await api.Delete($"/api/articles/{slug}/comments/abc", author.Token)).ExpectError(HttpStatusCode.NotFound);
        (await api.Delete($"/api/articles/{slug}/comments/999999999", author.Token)).ExpectError(HttpStatusCode.NotFound);

        (await api.Delete(path, author.Token)).Expect(HttpStatusCode.NoContent);
        Assert.Empty((await api.Get($"/api/articles/{slug}/comments")).Json["comments"]!.AsArray());
        (await api.Delete(path, author.Token)).ExpectError(HttpStatusCode.NotFound);
    }
}
