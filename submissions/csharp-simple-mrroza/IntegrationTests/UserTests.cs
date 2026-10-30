using System.Net;
using System.Text.Json.Nodes;
using Xunit;

namespace IntegrationTests;

[Collection(ConduitCollection.Name)]
public class UserTests(ConduitApp app)
{
    readonly ApiClient api = app.Client();

    [Fact]
    public async Task Register_returns_the_user_and_a_valid_token()
    {
        var name = Names.Unique("reg");
        var response = await api.Post("/api/users", new { user = new { username = name, email = $"{name}@example.com", password = "secret-1" } });

        response.Expect(HttpStatusCode.Created);
        var user = response.Json["user"]!.AsObject();
        Assert.Equal(["bio", "email", "image", "token", "username"], user.Select(field => field.Key).Order());
        Assert.Equal(name, user["username"]!.GetValue<string>());
        Assert.Null(user["bio"]);
        Assert.Null(user["image"]);

        var claims = Tokens.Claims(user["token"]!.GetValue<string>()).AsObject();
        Assert.Equal(["exp", "iat", "sub"], claims.Select(claim => claim.Key).Order());
        Assert.True(long.TryParse(claims["sub"]!.GetValue<string>(), out _));
        Assert.Equal(30 * 24 * 3600, claims["exp"]!.GetValue<long>() - claims["iat"]!.GetValue<long>());
    }

    [Fact]
    public async Task Register_rejects_a_taken_username_or_email()
    {
        var existing = await api.Register();
        var other = Names.Unique("other");

        (await api.Post("/api/users", new { user = new { username = existing.Username, email = $"{other}@example.com", password = "secret-1" } }))
            .ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Post("/api/users", new { user = new { username = other, email = existing.Email, password = "secret-1" } }))
            .ExpectError(HttpStatusCode.UnprocessableEntity);
    }

    [Theory]
    [InlineData("""{"user": {"email": "a@example.com", "password": "secret-1"}}""")]
    [InlineData("""{"user": {"username": "", "email": "a@example.com", "password": "secret-1"}}""")]
    [InlineData("""{"user": {"username": "a", "email": null, "password": "secret-1"}}""")]
    [InlineData("""{"user": {"username": 123, "email": "a@example.com", "password": "secret-1"}}""")]
    [InlineData("""{"username": "a", "email": "a@example.com", "password": "secret-1"}""")]
    [InlineData("""{"user": "a"}""")]
    [InlineData("""{"user": {"username": "a",""")]
    [InlineData("")]
    public async Task Register_rejects_invalid_bodies(string body) =>
        (await api.PostRaw("/api/users", body)).ExpectError(HttpStatusCode.UnprocessableEntity);

    [Fact]
    public async Task Register_rejects_a_password_over_72_bytes()
    {
        var name = Names.Unique("long");
        (await api.Post("/api/users", new { user = new { username = name, email = $"{name}@example.com", password = new string('p', 73) } }))
            .ExpectError(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Register_ignores_extra_fields()
    {
        var name = Names.Unique("extra");
        var response = await api.Post("/api/users", new { extra = 1, user = new { username = name, email = $"{name}@example.com", password = "secret-1", bio = "ignored" } });
        Assert.Null(response.Expect(HttpStatusCode.Created).Json["user"]!["bio"]);
    }

    [Fact]
    public async Task Login_checks_the_password()
    {
        var user = await api.Register();

        var response = await api.Post("/api/users/login", new { user = new { email = user.Email, password = user.Password } });
        Assert.Equal(user.Username, response.Expect(HttpStatusCode.OK).Json["user"]!["username"]!.GetValue<string>());

        (await api.Post("/api/users/login", new { user = new { email = user.Email, password = "wrong" } })).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Post("/api/users/login", new { user = new { email = "nobody@example.com", password = "wrong" } })).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Post("/api/users/login", new { user = new { email = user.Email } })).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Post("/api/users/login", new { email = user.Email, password = user.Password })).ExpectError(HttpStatusCode.UnprocessableEntity);
    }

    [Fact]
    public async Task Current_user_returns_the_presented_token()
    {
        var user = await api.Register();
        var response = (await api.Get("/api/user", user.Token)).Expect(HttpStatusCode.OK);
        Assert.Equal(user.Token, response.Json["user"]!["token"]!.GetValue<string>());
        Assert.Equal(user.Email, response.Json["user"]!["email"]!.GetValue<string>());
    }

    public static TheoryData<string> BadTokens()
    {
        var sub = "1";
        var now = Tokens.Now;
        return new TheoryData<string>
        {
            "garbage",
            "not.a.jwt",
            Tokens.Sign(new { sub, iat = now, exp = now + 3600 }, ConduitApp.JwtSecret + "-wrong"),
            Tokens.Sign(new { sub, iat = now - 7200, exp = now - 3600 }),
            Tokens.Sign(new { sub, iat = now }),
            Tokens.Sign(new { sub = 1, iat = now, exp = now + 3600 }),
            Tokens.Sign(new { sub = "abc", iat = now, exp = now + 3600 }),
            Tokens.Sign(new { sub, iat = now, exp = now + 3600 }, alg: "HS512"),
            Tokens.Unsigned(new { sub, iat = now, exp = now + 3600 }),
            Tokens.Sign(new { sub = "999999999", iat = now, exp = now + 3600 }),
        };
    }

    [Theory]
    [MemberData(nameof(BadTokens))]
    public async Task Current_user_rejects_bad_tokens(string token) =>
        (await api.Get("/api/user", token)).ExpectError(HttpStatusCode.Unauthorized);

    [Fact]
    public async Task Current_user_needs_the_token_scheme()
    {
        var user = await api.Register();
        (await api.Get("/api/user")).ExpectError(HttpStatusCode.Unauthorized);
        (await api.GetWithHeader("/api/user", $"Bearer {user.Token}")).ExpectError(HttpStatusCode.Unauthorized);
        (await api.GetWithHeader("/api/user", user.Token)).ExpectError(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Update_changes_only_the_given_fields()
    {
        var user = await api.Register();

        var updated = (await api.Put("/api/user", new { user = new { bio = "My bio", image = "https://example.com/me.png" } }, user.Token)).Expect(HttpStatusCode.OK);
        Assert.Equal("My bio", updated.Json["user"]!["bio"]!.GetValue<string>());
        Assert.Equal(user.Token, updated.Json["user"]!["token"]!.GetValue<string>());

        var nulls = new JsonObject { ["user"] = new JsonObject { ["bio"] = null, ["email"] = null } };
        var unchanged = (await api.Put("/api/user", nulls, user.Token)).Expect(HttpStatusCode.OK);
        Assert.Equal("My bio", unchanged.Json["user"]!["bio"]!.GetValue<string>());
        Assert.Equal(user.Email, unchanged.Json["user"]!["email"]!.GetValue<string>());

        var emptied = (await api.Put("/api/user", new { user = new { bio = "" } }, user.Token)).Expect(HttpStatusCode.OK);
        Assert.Equal("", emptied.Json["user"]!["bio"]!.GetValue<string>());
    }

    [Fact]
    public async Task Update_rejects_invalid_changes()
    {
        var user = await api.Register();
        var other = await api.Register();

        (await api.Put("/api/user", new { user = new { username = other.Username } }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Put("/api/user", new { user = new { email = other.Email } }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Put("/api/user", new { user = new { email = "" } }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Put("/api/user", new { user = new { bio = 5 } }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Put("/api/user", new { bio = "x" }, user.Token)).ExpectError(HttpStatusCode.UnprocessableEntity);
        (await api.Put("/api/user", new { user = new { bio = "x" } })).ExpectError(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_new_password_replaces_the_old_one()
    {
        var user = await api.Register();
        (await api.Put("/api/user", new { user = new { password = "new-password" } }, user.Token)).Expect(HttpStatusCode.OK);

        (await api.Post("/api/users/login", new { user = new { email = user.Email, password = user.Password } })).ExpectError(HttpStatusCode.Unauthorized);
        (await api.Post("/api/users/login", new { user = new { email = user.Email, password = "new-password" } })).Expect(HttpStatusCode.OK);
    }
}
