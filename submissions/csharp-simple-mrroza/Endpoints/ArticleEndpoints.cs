using System.Globalization;
using System.Security.Claims;
using Conduit.Auth;
using Conduit.Data;
using Conduit.Models;
using Dapper;
using Npgsql;

namespace Conduit.Endpoints;

public static class ArticleEndpoints
{
    public static void MapArticleEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/articles", List).AuthOptional();
        api.MapGet("/articles/feed", Feed).AuthRequired();
        api.MapGet("/articles/{slug}", Get).AuthOptional();
        api.MapPost("/articles", Create).AuthRequired();
        api.MapPut("/articles/{slug}", Update).AuthRequired();
        api.MapDelete("/articles/{slug}", Delete).AuthRequired();
        api.MapPost("/articles/{slug}/favorite", Favorite).AuthRequired();
        api.MapDelete("/articles/{slug}/favorite", Unfavorite).AuthRequired();
    }

    static async Task<IResult> List(HttpRequest http, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        if (!TryReadPage(http, out var limit, out var offset)) return ApiErrors.Validation("limit must be 1 to 100 and offset 0 or more");

        // Only the first filter given applies, in this order. An empty one counts as not given.
        var query = http.Query;
        var (page, count, filter) =
            query["tag"].ToString() is { Length: > 0 } tag ? (ArticleQueries.ByTag, ArticleQueries.CountByTag, tag)
            : query["author"].ToString() is { Length: > 0 } author ? (ArticleQueries.ByAuthor, ArticleQueries.CountByAuthor, author)
            : query["favorited"].ToString() is { Length: > 0 } favorited ? (ArticleQueries.ByFavorited, ArticleQueries.CountByFavorited, favorited)
            : (ArticleQueries.All, ArticleQueries.CountAll, null);

        await using var db = await data.OpenConnectionAsync();
        var rows = await db.QueryAsync<ArticleRow>(page, new { viewer = principal.UserId(), limit, offset, filter });
        var articlesCount = await db.ExecuteScalarAsync<long>(count, new { filter });
        return Results.Ok(new ArticleListResponse(rows.Select(row => row.ToArticle()), articlesCount));
    }

    static async Task<IResult> Feed(HttpRequest http, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        if (!TryReadPage(http, out var limit, out var offset)) return ApiErrors.Validation("limit must be 1 to 100 and offset 0 or more");

        var viewer = principal.UserId();
        await using var db = await data.OpenConnectionAsync();
        var rows = await db.QueryAsync<ArticleRow>(ArticleQueries.Feed, new { viewer, limit, offset });
        var articlesCount = await db.ExecuteScalarAsync<long>(ArticleQueries.CountFeed, new { viewer });
        return Results.Ok(new ArticleListResponse(rows.Select(row => row.ToArticle()), articlesCount));
    }

    static async Task<IResult> Get(string slug, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        await using var db = await data.OpenConnectionAsync();
        var article = await ArticleQueries.FindBySlug(db, principal.UserId(), slug);
        return article is null ? ApiErrors.NotFound("article not found") : Results.Ok(new ArticleResponse(article));
    }

    static async Task<IResult> Create(CreateArticleRequest request, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        var fields = request.Article;
        if (fields is null || string.IsNullOrEmpty(fields.Title) || string.IsNullOrEmpty(fields.Description) || string.IsNullOrEmpty(fields.Body))
            return ApiErrors.Validation("title, description and body are required");
        var tags = fields.TagList ?? [];
        if (tags.Any(string.IsNullOrEmpty)) return ApiErrors.Validation("tags can't be empty");

        var viewer = principal.UserId();
        await using var db = await data.OpenConnectionAsync();
        await using var transaction = await db.BeginTransactionAsync();
        var id = await db.ExecuteScalarAsync<long>($"""
            INSERT INTO articles (id, slug, author_id, title, description, body)
            SELECT n.id, {ArticleQueries.Slugify("@Title")} || '-' || n.id, @viewer, @Title, @Description, @Body
              FROM (SELECT nextval(pg_get_serial_sequence('articles', 'id')) AS id) n
            RETURNING id
            """, new { viewer, fields.Title, fields.Description, fields.Body }, transaction);
        if (tags.Length > 0)
        {
            // Sorted, so two articles adding the same new tags can't deadlock.
            await db.ExecuteAsync(
                "INSERT INTO tags (name) SELECT DISTINCT unnest(@tags::text[]) ORDER BY 1 ON CONFLICT (name) DO NOTHING",
                new { tags }, transaction);
            await db.ExecuteAsync(
                "INSERT INTO article_tags (article_id, tag_id) SELECT @id, t.id FROM tags t WHERE t.name = ANY(@tags::text[])",
                new { id, tags }, transaction);
        }
        await transaction.CommitAsync();

        var article = await ArticleQueries.FindById(db, viewer, id);
        return article is null
            ? ApiErrors.NotFound("article not found")
            : Results.Json(new ArticleResponse(article), statusCode: StatusCodes.Status201Created);
    }

    static async Task<IResult> Update(string slug, UpdateArticleRequest request, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        var fields = request.Article;
        if (fields is null) return ApiErrors.Validation("article is required");
        if (fields.Title == "" || fields.Description == "" || fields.Body == "") return ApiErrors.Validation("title, description and body can't be empty");

        var viewer = principal.UserId();
        await using var db = await data.OpenConnectionAsync();
        var row = await FindRow(db, slug);
        if (row is null) return ApiErrors.NotFound("article not found");
        if (row.AuthorId != viewer) return ApiErrors.Forbidden("only the author can do this");

        await db.ExecuteAsync($"""
            UPDATE articles SET title = COALESCE(@Title, title), description = COALESCE(@Description, description), body = COALESCE(@Body, body),
                   slug = CASE WHEN @Title::text IS NULL THEN slug ELSE {ArticleQueries.Slugify("@Title")} || '-' || id END,
                   updated_at = now()
             WHERE id = @id
            """, new { id = row.Id, fields.Title, fields.Description, fields.Body });
        var article = await ArticleQueries.FindById(db, viewer, row.Id);
        return article is null ? ApiErrors.NotFound("article not found") : Results.Ok(new ArticleResponse(article));
    }

    static async Task<IResult> Delete(string slug, ClaimsPrincipal principal, NpgsqlDataSource data)
    {
        await using var db = await data.OpenConnectionAsync();
        var row = await FindRow(db, slug);
        if (row is null) return ApiErrors.NotFound("article not found");
        if (row.AuthorId != principal.UserId()) return ApiErrors.Forbidden("only the author can do this");

        await db.ExecuteAsync("DELETE FROM articles WHERE id = @id", new { id = row.Id });
        return Results.NoContent();
    }

    static Task<IResult> Favorite(string slug, ClaimsPrincipal principal, NpgsqlDataSource data) =>
        SetFavorite(slug, principal, data, "INSERT INTO favorites (user_id, article_id) VALUES (@viewer, @id) ON CONFLICT DO NOTHING");

    static Task<IResult> Unfavorite(string slug, ClaimsPrincipal principal, NpgsqlDataSource data) =>
        SetFavorite(slug, principal, data, "DELETE FROM favorites WHERE user_id = @viewer AND article_id = @id");

    static async Task<IResult> SetFavorite(string slug, ClaimsPrincipal principal, NpgsqlDataSource data, string sql)
    {
        var viewer = principal.UserId();
        await using var db = await data.OpenConnectionAsync();
        var row = await FindRow(db, slug);
        if (row is null) return ApiErrors.NotFound("article not found");

        await db.ExecuteAsync(sql, new { viewer, id = row.Id });
        var article = await ArticleQueries.FindById(db, viewer, row.Id);
        return article is null ? ApiErrors.NotFound("article not found") : Results.Ok(new ArticleResponse(article));
    }

    static Task<ArticleRow?> FindRow(NpgsqlConnection db, string slug) =>
        db.QuerySingleOrDefaultAsync<ArticleRow>("SELECT id, author_id FROM articles WHERE slug = @slug", new { slug });

    // Parsed by hand: normal binding would accept "+5" and " 5", and treat "limit=" as absent.
    static bool TryReadPage(HttpRequest http, out int limit, out int offset)
    {
        limit = 20;
        offset = 0;
        return TryReadNumber(http, "limit", ref limit) && limit is >= 1 and <= 100 && TryReadNumber(http, "offset", ref offset);
    }

    static bool TryReadNumber(HttpRequest http, string name, ref int value) =>
        !http.Query.TryGetValue(name, out var text)
        || int.TryParse(text.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
