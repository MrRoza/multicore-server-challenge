using Conduit.Articles.Abstractions;
using Conduit.Shared.Data;
using Dapper;

namespace Conduit.Articles.Data.Postgres;

public sealed class ArticleRepository(IUnitOfWork work) : IArticleRepository
{
    public async Task<ArticleListResponseDto> List(long viewer, Page page, string? tag, string? author, string? favorited)
    {
        // Only the first filter given applies, in this order. An empty one counts as not given.
        var (pageSql, countSql, filter) =
            !string.IsNullOrEmpty(tag) ? (ArticleSql.ByTag, ArticleSql.CountByTag, tag)
            : !string.IsNullOrEmpty(author) ? (ArticleSql.ByAuthor, ArticleSql.CountByAuthor, author)
            : !string.IsNullOrEmpty(favorited) ? (ArticleSql.ByFavorited, ArticleSql.CountByFavorited, favorited)
            : (ArticleSql.All, ArticleSql.CountAll, null);

        var rows = await work.Connection.QueryAsync<ArticleRowDto>(pageSql, new { Viewer = viewer, page.Limit, page.Offset, filter });
        var articlesCount = await work.Connection.ExecuteScalarAsync<long>(countSql, new { filter });
        return new ArticleListResponseDto(rows.Select(row => row.ToArticle()), articlesCount);
    }

    public async Task<ArticleListResponseDto> Feed(long viewer, Page page)
    {
        var rows = await work.Connection.QueryAsync<ArticleRowDto>(ArticleSql.Feed, new { Viewer = viewer, page.Limit, page.Offset });
        var articlesCount = await work.Connection.ExecuteScalarAsync<long>(ArticleSql.CountFeed, new { Viewer = viewer });
        return new ArticleListResponseDto(rows.Select(row => row.ToArticle()), articlesCount);
    }

    public Task<ArticleDto?> FindBySlug(long viewer, string slug) =>
        FindOne(ArticleSql.FullSelect + " WHERE a.slug = @slug", new { Viewer = viewer, slug });

    public Task<ArticleDto?> FindById(long viewer, long id) =>
        FindOne(ArticleSql.FullSelect + " WHERE a.id = @id", new { Viewer = viewer, id });

    async Task<ArticleDto?> FindOne(string sql, object args)
    {
        var row = await work.Connection.QuerySingleOrDefaultAsync<ArticleRowDto>(sql, args);
        return row?.ToArticle();
    }

    public async Task<long?> FindId(string slug)
    {
        return await work.Connection.QuerySingleOrDefaultAsync<long?>("SELECT id FROM articles WHERE slug = @slug", new { slug });
    }

    public async Task<ArticleOwnerDto?> FindOwner(string slug)
    {
        return await work.Connection.QuerySingleOrDefaultAsync<ArticleOwnerDto>("SELECT id, author_id FROM articles WHERE slug = @slug", new { slug });
    }

    public async Task<long> Create(long viewer, string title, string description, string body, string[] tags)
    {
        var id = await work.Connection.ExecuteScalarAsync<long>($"""
            INSERT INTO articles (id, slug, author_id, title, description, body)
            SELECT n.id, {ArticleSql.Slugify("@title")} || '-' || n.id, @viewer, @title, @description, @body
              FROM (SELECT nextval(pg_get_serial_sequence('articles', 'id')) AS id) n
            RETURNING id
            """, new { viewer, title, description, body });
        if (tags.Length > 0)
        {
            // Sorted, so two articles adding the same new tags can't deadlock.
            await work.Connection.ExecuteAsync(
                "INSERT INTO tags (name) SELECT DISTINCT unnest(@tags::text[]) ORDER BY 1 ON CONFLICT (name) DO NOTHING",
                new { tags });
            await work.Connection.ExecuteAsync(
                "INSERT INTO article_tags (article_id, tag_id) SELECT @id, t.id FROM tags t WHERE t.name = ANY(@tags::text[])",
                new { id, tags });
        }
        return id;
    }

    public async Task Update(long id, string? title, string? description, string? body)
    {
        await work.Connection.ExecuteAsync($"""
            UPDATE articles SET title = COALESCE(@title, title), description = COALESCE(@description, description), body = COALESCE(@body, body),
                   slug = CASE WHEN @title::text IS NULL THEN slug ELSE {ArticleSql.Slugify("@title")} || '-' || id END,
                   updated_at = now()
             WHERE id = @id
            """, new { id, title, description, body });
    }

    public async Task Delete(long id)
    {
        await work.Connection.ExecuteAsync("DELETE FROM articles WHERE id = @id", new { id });
    }

    public async Task Favorite(long viewer, long id)
    {
        await work.Connection.ExecuteAsync("INSERT INTO favorites (user_id, article_id) VALUES (@viewer, @id) ON CONFLICT DO NOTHING", new { viewer, id });
    }

    public async Task Unfavorite(long viewer, long id)
    {
        await work.Connection.ExecuteAsync("DELETE FROM favorites WHERE user_id = @viewer AND article_id = @id", new { viewer, id });
    }
}
