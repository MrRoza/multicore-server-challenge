using System.Globalization;
using System.Text.Json.Serialization;
using Conduit.Features.Profiles;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public record Article(
    string Slug,
    string Title,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body, // lists leave it out
    string[] TagList,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Favorited,
    long FavoritesCount,
    Profile Author);

public record ArticleResponse(Article Article);

public record ArticleList(IEnumerable<Article> Articles, long ArticlesCount);

public class ArticleRow : ProfileRow
{
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Body { get; init; }
    public string[] TagList { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool Favorited { get; init; }
    public long FavoritesCount { get; init; }

    public Article ToArticle() =>
        new(Slug, Title, Description, Body, TagList, CreatedAt, UpdatedAt, Favorited, FavoritesCount, ToProfile());
}

/// <summary>What commands look up before changing an article.</summary>
public static class ArticleLookup
{
    public static Task<long?> FindId(NpgsqlConnection db, string slug) =>
        db.QuerySingleOrDefaultAsync<long?>("SELECT id FROM articles WHERE slug = @slug", new { slug });

    /// <summary>The id of an article the viewer wrote: 404 if it doesn't exist, 403 if someone else wrote it.</summary>
    public static async Task<Result<long>> FindOwnId(NpgsqlConnection db, string slug, long viewer)
    {
        var row = await db.QuerySingleOrDefaultAsync<(long Id, long AuthorId)?>(
            "SELECT id, author_id FROM articles WHERE slug = @slug", new { slug });
        if (row is null) return Error.NotFound("article not found");
        if (row.Value.AuthorId != viewer) return Error.Forbidden("only the author can do this");
        return row.Value.Id;
    }
}

public static class Paging
{
    // Parsed by hand: normal binding would accept "+5" and " 5", and treat "limit=" as absent.
    public static bool TryRead(HttpRequest http, out int limit, out int offset)
    {
        limit = 20;
        offset = 0;
        return TryReadNumber(http, "limit", ref limit) && limit is >= 1 and <= 100 && TryReadNumber(http, "offset", ref offset);
    }

    static bool TryReadNumber(HttpRequest http, string name, ref int value) =>
        !http.Query.TryGetValue(name, out var text)
        || int.TryParse(text.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out value);

    public static readonly Error Invalid = Error.Validation("limit must be 1 to 100 and offset 0 or more");
}
