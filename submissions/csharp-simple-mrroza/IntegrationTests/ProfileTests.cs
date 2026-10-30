using System.Net;
using Xunit;

namespace IntegrationTests;

[Collection(ConduitCollection.Name)]
public class ProfileTests(ConduitApp app)
{
    readonly ApiClient api = app.Client();

    [Fact]
    public async Task Get_returns_the_profile()
    {
        var user = await api.Register();
        var profile = (await api.Get($"/api/profiles/{user.Username}")).Expect(HttpStatusCode.OK).Json["profile"]!.AsObject();

        Assert.Equal(["bio", "following", "image", "username"], profile.Select(field => field.Key).Order());
        Assert.Equal(user.Username, profile["username"]!.GetValue<string>());
        Assert.False(profile["following"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Get_handles_unknown_users_and_invalid_tokens()
    {
        var user = await api.Register();
        (await api.Get("/api/profiles/no_such_user")).ExpectError(HttpStatusCode.NotFound);
        (await api.Get($"/api/profiles/{user.Username}", "garbage")).ExpectError(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Follow_and_unfollow_can_be_repeated()
    {
        var follower = await api.Register();
        var author = await api.Register();
        var path = $"/api/profiles/{author.Username}/follow";

        foreach (var _ in Enumerable.Range(0, 2))
            Assert.True((await api.Post(path, token: follower.Token)).Expect(HttpStatusCode.OK).Json["profile"]!["following"]!.GetValue<bool>());
        Assert.True((await api.Get($"/api/profiles/{author.Username}", follower.Token)).Expect(HttpStatusCode.OK).Json["profile"]!["following"]!.GetValue<bool>());
        Assert.False((await api.Get($"/api/profiles/{author.Username}", author.Token)).Expect(HttpStatusCode.OK).Json["profile"]!["following"]!.GetValue<bool>());

        foreach (var _ in Enumerable.Range(0, 2))
            Assert.False((await api.Delete(path, follower.Token)).Expect(HttpStatusCode.OK).Json["profile"]!["following"]!.GetValue<bool>());
        Assert.False((await api.Get($"/api/profiles/{author.Username}", follower.Token)).Expect(HttpStatusCode.OK).Json["profile"]!["following"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Follow_needs_auth_and_a_known_user()
    {
        var user = await api.Register();
        (await api.Post($"/api/profiles/{user.Username}/follow")).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Delete($"/api/profiles/{user.Username}/follow")).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Post("/api/profiles/no_such_user/follow", token: user.Token)).ExpectError(HttpStatusCode.NotFound);
        (await api.Delete("/api/profiles/no_such_user/follow", user.Token)).ExpectError(HttpStatusCode.NotFound);
    }
}
