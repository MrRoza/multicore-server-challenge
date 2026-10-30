using System.Globalization;
using System.Security.Claims;
using Conduit.Auth;
using Conduit.Models;
using Dapper;
using Npgsql;

namespace Conduit.Endpoints;

public static class CommentEndpoints
{
    public static void MapCommentEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/articles/{slug}/comments", List).AuthOptional();
        api.MapPost("/articles/{slug}/comments", Create).AuthRequired();
        api.MapDelete("/articles/{slug}/comments/{id}", Delete).AuthRequired();
    }

    static async Task<IResult> List(string slug, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        await using var db = await data.OpenConnectionAsync();
        var articleId = await FindArticleId(db, slug);
        if (articleId is null) return ApiErrors.NotFound("article not found");

        var rows = await db.QueryAsync<CommentRow>("""
            SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image,
                   EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @viewer AND fo.followee_id = c.author_id) AS following
              FROM comments c JOIN users u ON u.id = c.author_id
             WHERE c.article_id = @articleId
             ORDER BY c.created_at DESC, c.id DESC
             LIMIT 100
            """, new { viewer = principal.UserId(), articleId });
        return Results.Ok(new CommentListResponse(rows.Select(row => row.ToComment())));
    }

    static async Task<IResult> Create(string slug, CreateCommentRequest request, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        var body = request.Comment?.Body;
        if (string.IsNullOrEmpty(body)) return ApiErrors.Validation("body is required");

        await using var db = await data.OpenConnectionAsync();
        var articleId = await FindArticleId(db, slug);
        if (articleId is null) return ApiErrors.NotFound("article not found");

        var row = await db.QuerySingleAsync<CommentRow>("""
            WITH c AS (INSERT INTO comments (article_id, author_id, body) VALUES (@articleId, @viewer, @body)
                       RETURNING id, body, created_at, updated_at, author_id)
            SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image
              FROM c JOIN users u ON u.id = c.author_id
            """, new { articleId, viewer = principal.UserId(), body });
        return Results.Json(new CommentResponse(row.ToComment()), statusCode: StatusCodes.Status201Created);
    }

    static async Task<IResult> Delete(string slug, string id, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        if (!long.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var commentId)) return ApiErrors.NotFound("comment not found");

        await using var db = await data.OpenConnectionAsync();
        var authorId = await db.QuerySingleOrDefaultAsync<long?>("""
            SELECT c.author_id FROM comments c JOIN articles a ON a.id = c.article_id
             WHERE c.id = @commentId AND a.slug = @slug
            """, new { commentId, slug });
        if (authorId is null) return ApiErrors.NotFound("comment not found");
        if (authorId != principal.UserId()) return ApiErrors.Forbidden("only the author can do this");

        await db.ExecuteAsync("DELETE FROM comments WHERE id = @commentId", new { commentId });
        return Results.NoContent();
    }

    static Task<long?> FindArticleId(NpgsqlConnection db, string slug) =>
        db.QuerySingleOrDefaultAsync<long?>("SELECT id FROM articles WHERE slug = @slug", new { slug });
}
