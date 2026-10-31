using Dapper;
using Npgsql;

namespace Conduit.Comments;

/// <summary>Finds an article in the shared database, so comments don't have to call the Articles service.</summary>
public static class ArticleIds
{
    public static Task<long?> Find(NpgsqlConnection db, string slug) =>
        db.QuerySingleOrDefaultAsync<long?>("SELECT id FROM articles WHERE slug = @slug", new { slug });
}
