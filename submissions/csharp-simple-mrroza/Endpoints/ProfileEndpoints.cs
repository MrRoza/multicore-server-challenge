using System.Security.Claims;
using Conduit.Auth;
using Conduit.Models;
using Dapper;
using Npgsql;

namespace Conduit.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/profiles/{username}", Get).AuthOptional();
        api.MapPost("/profiles/{username}/follow", Follow).AuthRequired();
        api.MapDelete("/profiles/{username}/follow", Unfollow).AuthRequired();
    }

    static async Task<IResult> Get(string username, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        await using var db = await data.OpenConnectionAsync();
        var row = await db.QuerySingleOrDefaultAsync<AuthorRow>("""
            SELECT u.id, u.username, u.bio, u.image,
                   EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @viewer AND fo.followee_id = u.id) AS following
              FROM users u WHERE u.username = @username
            """, new { viewer = principal.UserId(), username });
        return row is null ? ApiErrors.NotFound("user not found") : Results.Ok(new ProfileResponse(row.ToProfile()));
    }

    static Task<IResult> Follow(string username, ClaimsPrincipal principal, NpgsqlDataSource data) =>
        SetFollowing(username, principal, data, true,
            "INSERT INTO follows (follower_id, followee_id) VALUES (@viewer, @id) ON CONFLICT DO NOTHING");

    static Task<IResult> Unfollow(string username, ClaimsPrincipal principal, NpgsqlDataSource data) =>
        SetFollowing(username, principal, data, false,
            "DELETE FROM follows WHERE follower_id = @viewer AND followee_id = @id");

    static async Task<IResult> SetFollowing(string username, ClaimsPrincipal principal, NpgsqlDataSource data, bool following, string sql)
    {
        await using var db = await data.OpenConnectionAsync();
        var row = await db.QuerySingleOrDefaultAsync<AuthorRow>(
            "SELECT id, username, bio, image FROM users WHERE username = @username", new { username });
        if (row is null) return ApiErrors.NotFound("user not found");

        await db.ExecuteAsync(sql, new { viewer = principal.UserId(), id = row.Id });
        return Results.Ok(new ProfileResponse(row.ToProfile() with { Following = following }));
    }
}
