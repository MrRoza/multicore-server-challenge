using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace IntegrationTests;

[Collection(ConduitCollection.Name)]
public partial class ArticleTests(ConduitApp app)
{
    readonly ApiClient api = app.Client();

    [GeneratedRegex(@"^\d{4}-\d\d-\d\dT\d\d:\d\d:\d\d\.\d{3}Z$")]
    private static partial Regex Timestamp();

    static string[] Slugs(ApiResponse list) =>
        list.Json["articles"]!.AsArray().Select(article => article!["slug"]!.GetValue<string>()).ToArray();

    static long Count(ApiResponse list) => list.Json["articlesCount"]!.GetValue<long>();

    [Fact]
    public async Task Create_returns_the_full_article()
    {
        var author = await api.Register();
        var tag = Names.Unique("tag");
        var article = await api.CreateArticle(author.Token, "Hello, World! 2026", [tag, "alpha" + tag, tag]);

        Assert.Equal(
            ["author", "body", "createdAt", "description", "favorited", "favoritesCount", "slug", "tagList", "title", "updatedAt"],
            article.AsObject().Select(field => field.Key).Order());
        Assert.Matches(@"^hello-world-2026-\d+$", article["slug"]!.GetValue<string>());
        Assert.Equal(["alpha" + tag, tag], article["tagList"]!.AsArray().Select(name => name!.GetValue<string>()));
        Assert.Equal(0, article["favoritesCount"]!.GetValue<long>());
        Assert.False(article["favorited"]!.GetValue<bool>());
        Assert.Matches(Timestamp(), article["createdAt"]!.GetValue<string>());
        Assert.Equal(article["createdAt"]!.GetValue<string>(), article["updatedAt"]!.GetValue<string>());
        Assert.Equal(author.Username, article["author"]!["username"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("""{"article": {"description": "d", "body": "b"}}""")]
    [InlineData("""{"article": {"title": "t", "description": "", "body": "b"}}""")]
    [InlineData("""{"article": {"title": "t", "description": "d", "body": null}}""")]
    [InlineData("""{"article": {"title": 7, "description": "d", "body": "b"}}""")]
    [InlineData("""{"article": {"title": "t", "description": "d", "body": "b", "tagList": "x"}}""")]
    [InlineData("""{"article": {"title": "t", "description": "d", "body": "b", "tagList": ["ok", ""]}}""")]
    [InlineData("""{"article": {"title": "t", "description": "d", "body": "b", "tagList": [1]}}""")]
    [InlineData("""{"article": {"title": "t", "description": "d", "body": "b", "tagList": [null]}}""")]
    [InlineData("""{"title": "t", "description": "d", "body": "b"}""")]
    [InlineData("""{"article": """)]
    public async Task Create_rejects_invalid_bodies(string body)
    {
        var author = await api.Register();
        (await api.PostRaw("/api/articles", body, author.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Create_treats_absent_and_null_tags_as_none()
    {
        var author = await api.Register();
        var fields = new JsonObject { ["title"] = "No tags", ["description"] = "d", ["body"] = "b" };
        var absent = await api.Post("/api/articles", new JsonObject { ["article"] = fields.DeepClone() }, author.Token);
        Assert.Empty(absent.Expect(HttpStatusCode.Created).Json["article"]!["tagList"]!.AsArray());

        fields["tagList"] = null;
        var isNull = await api.Post("/api/articles", new JsonObject { ["article"] = fields.DeepClone() }, author.Token);
        Assert.Empty(isNull.Expect(HttpStatusCode.Created).Json["article"]!["tagList"]!.AsArray());
    }

    [Fact]
    public async Task Writes_need_auth()
    {
        var author = await api.Register();
        var slug = (await api.CreateArticle(author.Token))["slug"]!.GetValue<string>();
        var article = new { title = "t", description = "d", body = "b" };

        (await api.Post("/api/articles", new { article })).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Put($"/api/articles/{slug}", new { article })).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Delete($"/api/articles/{slug}")).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Post($"/api/articles/{slug}/favorite")).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Delete($"/api/articles/{slug}/favorite")).ExpectError(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Get_handles_unknown_slugs_and_invalid_tokens()
    {
        var author = await api.Register();
        var slug = (await api.CreateArticle(author.Token))["slug"]!.GetValue<string>();
        (await api.Get("/api/articles/no-such-article")).ExpectError(HttpStatusCode.NotFound);
        (await api.Get($"/api/articles/{slug}", "garbage")).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Get($"/api/articles/{slug}")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Update_renames_the_slug_only_with_a_new_title()
    {
        var author = await api.Register();
        var slug = (await api.CreateArticle(author.Token, "First Title"))["slug"]!.GetValue<string>();
        var id = slug.Split('-')[^1];

        var body = (await api.Put($"/api/articles/{slug}", new { article = new { body = "New body" } }, author.Token)).Expect(HttpStatusCode.OK).Json["article"]!;
        Assert.Equal(slug, body["slug"]!.GetValue<string>());
        Assert.Equal("New body", body["body"]!.GetValue<string>());
        Assert.Equal("First Title", body["title"]!.GetValue<string>());

        var renamed = (await api.Put($"/api/articles/{slug}", new { article = new { title = "  Second -- Title!  " } }, author.Token)).Expect(HttpStatusCode.OK).Json["article"]!;
        Assert.Equal($"second-title-{id}", renamed["slug"]!.GetValue<string>());
        Assert.Equal("  Second -- Title!  ", renamed["title"]!.GetValue<string>());
        Assert.True(string.CompareOrdinal(renamed["updatedAt"]!.GetValue<string>(), renamed["createdAt"]!.GetValue<string>()) >= 0);

        (await api.Get($"/api/articles/{slug}")).ExpectError(HttpStatusCode.NotFound);
        (await api.Get($"/api/articles/second-title-{id}")).Expect(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Only_the_author_can_change_an_article()
    {
        var author = await api.Register();
        var other = await api.Register();
        var slug = (await api.CreateArticle(author.Token))["slug"]!.GetValue<string>();

        (await api.Put($"/api/articles/{slug}", new { article = new { body = "x" } }, other.Token)).ExpectError(HttpStatusCode.Forbidden);
        (await api.Delete($"/api/articles/{slug}", other.Token)).ExpectError(HttpStatusCode.Forbidden);
        (await api.Put("/api/articles/no-such-article", new { article = new { body = "x" } }, author.Token)).ExpectError(HttpStatusCode.NotFound);
        (await api.Delete("/api/articles/no-such-article", author.Token)).ExpectError(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Update_rejects_invalid_changes()
    {
        var author = await api.Register();
        var slug = (await api.CreateArticle(author.Token))["slug"]!.GetValue<string>();

        (await api.Put($"/api/articles/{slug}", new { article = new { title = "" } }, author.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Put($"/api/articles/{slug}", new { article = new { body = 1 } }, author.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Put($"/api/articles/{slug}", new { body = "x" }, author.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Delete_removes_the_article()
    {
        var author = await api.Register();
        var slug = (await api.CreateArticle(author.Token))["slug"]!.GetValue<string>();

        (await api.Delete($"/api/articles/{slug}", author.Token)).Expect(HttpStatusCode.NoContent);
        (await api.Get($"/api/articles/{slug}")).ExpectError(HttpStatusCode.NotFound);
        (await api.Delete($"/api/articles/{slug}", author.Token)).ExpectError(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Favorite_and_unfavorite_can_be_repeated()
    {
        var author = await api.Register();
        var reader = await api.Register();
        var slug = (await api.CreateArticle(author.Token))["slug"]!.GetValue<string>();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var favorited = (await api.Post($"/api/articles/{slug}/favorite", token: reader.Token)).Expect(HttpStatusCode.OK).Json["article"]!;
            Assert.True(favorited["favorited"]!.GetValue<bool>());
            Assert.Equal(1, favorited["favoritesCount"]!.GetValue<long>());
        }
        var seenByAuthor = (await api.Get($"/api/articles/{slug}", author.Token)).Expect(HttpStatusCode.OK).Json["article"]!;
        Assert.False(seenByAuthor["favorited"]!.GetValue<bool>());
        Assert.Equal(1, seenByAuthor["favoritesCount"]!.GetValue<long>());

        foreach (var _ in Enumerable.Range(0, 2))
        {
            var removed = (await api.Delete($"/api/articles/{slug}/favorite", reader.Token)).Expect(HttpStatusCode.OK).Json["article"]!;
            Assert.False(removed["favorited"]!.GetValue<bool>());
            Assert.Equal(0, removed["favoritesCount"]!.GetValue<long>());
        }
        (await api.Post("/api/articles/no-such-article/favorite", token: reader.Token)).ExpectError(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lists_filter_by_author_tag_and_favorited_newest_first()
    {
        var author = await api.Register();
        var fan = await api.Register();
        var tag = Names.Unique("tag");
        var first = (await api.CreateArticle(author.Token, tags: [tag]))["slug"]!.GetValue<string>();
        var second = (await api.CreateArticle(author.Token, tags: [tag]))["slug"]!.GetValue<string>();
        await api.Post($"/api/articles/{first}/favorite", token: fan.Token);

        var byAuthor = (await api.Get($"/api/articles?author={author.Username}")).Expect(HttpStatusCode.OK);
        Assert.Equal([second, first], Slugs(byAuthor));
        Assert.Equal(2, Count(byAuthor));
        Assert.All(byAuthor.Json["articles"]!.AsArray(), article => Assert.Null(article!["body"]));

        var byTag = (await api.Get($"/api/articles?tag={tag}")).Expect(HttpStatusCode.OK);
        Assert.Equal([second, first], Slugs(byTag));
        Assert.Equal(2, Count(byTag));

        var byFavorited = (await api.Get($"/api/articles?favorited={fan.Username}", fan.Token)).Expect(HttpStatusCode.OK);
        Assert.Equal([first], Slugs(byFavorited));
        Assert.True(byFavorited.Json["articles"]![0]!["favorited"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Lists_apply_only_the_first_filter()
    {
        var author = await api.Register();
        var other = await api.Register();
        var tag = Names.Unique("tag");
        var tagged = (await api.CreateArticle(author.Token, tags: [tag]))["slug"]!.GetValue<string>();
        var untagged = (await api.CreateArticle(other.Token))["slug"]!.GetValue<string>();

        Assert.Equal([tagged], Slugs(await api.Get($"/api/articles?author={other.Username}&tag={tag}")));
        Assert.Equal([untagged], Slugs(await api.Get($"/api/articles?tag=&author={other.Username}")));
        Assert.Equal([untagged], Slugs(await api.Get($"/api/articles?favorited={author.Username}&author={other.Username}")));
    }

    [Theory]
    [InlineData("tag=no_such_tag")]
    [InlineData("author=no_such_user")]
    [InlineData("favorited=no_such_user")]
    public async Task Lists_of_unknown_filters_are_empty(string query)
    {
        var list = (await api.Get($"/api/articles?{query}")).Expect(HttpStatusCode.OK);
        Assert.Empty(Slugs(list));
        Assert.Equal(0, Count(list));
    }

    [Fact]
    public async Task Lists_page_with_limit_and_offset()
    {
        var author = await api.Register();
        var slugs = new List<string>();
        for (var i = 0; i < 3; i++) slugs.Insert(0, (await api.CreateArticle(author.Token))["slug"]!.GetValue<string>());
        var byAuthor = $"/api/articles?author={author.Username}";

        Assert.Equal(slugs[..1], Slugs(await api.Get($"{byAuthor}&limit=1")));
        Assert.Equal(slugs[1..2], Slugs(await api.Get($"{byAuthor}&limit=1&offset=1")));
        Assert.Equal(slugs[1..2], Slugs(await api.Get($"{byAuthor}&limit=01&offset=001")));
        var pastTheEnd = await api.Get($"{byAuthor}&offset=100000000");
        Assert.Empty(Slugs(pastTheEnd));
        Assert.Equal(3, Count(pastTheEnd));
    }

    [Theory]
    [InlineData("limit=0")]
    [InlineData("limit=101")]
    [InlineData("limit=-1")]
    [InlineData("limit=abc")]
    [InlineData("limit=")]
    [InlineData("limit=1.5")]
    [InlineData("limit=%2B5")]
    [InlineData("limit=%205")]
    [InlineData("offset=-1")]
    [InlineData("offset=abc")]
    [InlineData("offset=")]
    [InlineData("offset=99999999999999999999")]
    public async Task Lists_reject_invalid_paging(string query)
    {
        var user = await api.Register();
        (await api.Get($"/api/articles?{query}")).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Get($"/api/articles/feed?{query}", user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Feed_shows_followed_authors_only()
    {
        var reader = await api.Register();
        var followed = await api.Register();
        var other = await api.Register();
        var first = (await api.CreateArticle(followed.Token))["slug"]!.GetValue<string>();
        var second = (await api.CreateArticle(followed.Token))["slug"]!.GetValue<string>();
        await api.CreateArticle(other.Token);

        var empty = (await api.Get("/api/articles/feed", reader.Token)).Expect(HttpStatusCode.OK);
        Assert.Empty(Slugs(empty));
        Assert.Equal(0, Count(empty));

        await api.Post($"/api/profiles/{followed.Username}/follow", token: reader.Token);
        var feed = (await api.Get("/api/articles/feed", reader.Token)).Expect(HttpStatusCode.OK);
        Assert.Equal([second, first], Slugs(feed));
        Assert.Equal(2, Count(feed));
        Assert.All(feed.Json["articles"]!.AsArray(), article => Assert.True(article!["author"]!["following"]!.GetValue<bool>()));
        Assert.Equal([first], Slugs(await api.Get("/api/articles/feed?limit=1&offset=1", reader.Token)));

        (await api.Get("/api/articles/feed")).ExpectError(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task List_counts_are_capped_at_1000()
    {
        var author = await api.Register();
        for (var i = 0; i < 1001; i++) await api.CreateArticle(author.Token, "Title");

        var list = (await api.Get($"/api/articles?author={author.Username}")).Expect(HttpStatusCode.OK);

        Assert.Equal(1000, Count(list));
        Assert.Equal(20, Slugs(list).Length);
    }

    [Fact]
    public async Task Lists_reject_invalid_tokens() =>
        (await api.Get("/api/articles", "garbage")).ExpectError(HttpStatusCode.Unauthorized);
}
