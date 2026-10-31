using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Articles;

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
