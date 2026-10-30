using System.Net;
using Xunit;

namespace IntegrationTests;

[Collection(ConduitCollection.Name)]
public class TagAndRoutingTests(ConduitApp app)
{
    readonly ApiClient api = app.Client();

    [Fact]
    public async Task Tags_are_sorted_and_include_new_ones()
    {
        var author = await api.Register();
        var tag = Names.Unique("tag");
        await api.CreateArticle(author.Token, tags: [tag]);

        var tags = (await api.Get("/api/tags")).Expect(HttpStatusCode.OK).Json["tags"]!.AsArray().Select(name => name!.GetValue<string>()).ToArray();

        Assert.Contains(tag, tags);
        Assert.Equal(tags.Order(StringComparer.Ordinal), tags);
    }

    [Fact]
    public async Task Tags_stay_after_their_article_is_deleted()
    {
        var author = await api.Register();
        var tag = Names.Unique("tag");
        var slug = (await api.CreateArticle(author.Token, tags: [tag]))["slug"]!.GetValue<string>();

        (await api.Delete($"/api/articles/{slug}", author.Token)).Expect(HttpStatusCode.NoContent);

        Assert.Contains(tag, (await api.Get("/api/tags")).Json["tags"]!.AsArray().Select(name => name!.GetValue<string>()));
    }

    [Fact]
    public async Task Tags_ignore_the_auth_header() =>
        (await api.Get("/api/tags", "garbage")).Expect(HttpStatusCode.OK);

    [Fact]
    public async Task Unknown_routes_are_404_in_the_error_shape() =>
        (await api.Get("/api/no-such-route")).ExpectError(HttpStatusCode.NotFound);
}
