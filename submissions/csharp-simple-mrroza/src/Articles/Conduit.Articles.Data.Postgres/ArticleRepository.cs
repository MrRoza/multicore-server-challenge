using Conduit.Articles.Abstractions;
using Conduit.Shared.Data.Postgres;

namespace Conduit.Articles.Data.Postgres;

public sealed class ArticleRepository(PostgresUnitOfWork work) : PostgresRepository(work), IArticleRepository
{
    public async Task<ArticleListResponseDto> List(long viewer, Page page, string? tag, string? author, string? favorited)
    {
        // Only the first filter given applies, in this order. An empty one counts as not given.
        var (pageSql, countSql, filter) =
            !string.IsNullOrEmpty(tag) ? (ArticleSql.ByTag, ArticleSql.CountByTag, tag)
            : !string.IsNullOrEmpty(author) ? (ArticleSql.ByAuthor, ArticleSql.CountByAuthor, author)
            : !string.IsNullOrEmpty(favorited) ? (ArticleSql.ByFavorited, ArticleSql.CountByFavorited, favorited)
            : (ArticleSql.All, ArticleSql.CountAll, null);

        var (rows, counts) = await QueryBoth<ArticleRowDto, long>(pageSql, countSql, new { Viewer = viewer, page.Limit, page.Offset, filter });
        return new ArticleListResponseDto(rows.Select(row => row.ToArticle()), counts.Single());
    }

    public async Task<ArticleListResponseDto> Feed(long viewer, Page page)
    {
        var (rows, counts) = await QueryBoth<ArticleRowDto, long>(ArticleSql.Feed, ArticleSql.CountFeed, new { Viewer = viewer, page.Limit, page.Offset });
        return new ArticleListResponseDto(rows.Select(row => row.ToArticle()), counts.Single());
    }

    public Task<ArticleDto?> FindBySlug(long viewer, string slug) =>
        FindOne(ArticleSql.FullSelect + " WHERE a.slug = @slug", new { Viewer = viewer, slug });

    public Task<ArticleDto?> FindById(long viewer, long id) =>
        FindOne(ArticleSql.FullSelect + " WHERE a.id = @id", new { Viewer = viewer, id });

    async Task<ArticleDto?> FindOne(string sql, object args)
    {
        var row = await QuerySingleOrDefault<ArticleRowDto>(sql, args);
        return row?.ToArticle();
    }

    public async Task<long?> FindId(string slug)
    {
        return await QuerySingleOrDefault<long?>("SELECT id FROM articles WHERE slug = @slug", new { slug });
    }

    public async Task<ArticleOwnerDto?> FindOwner(string slug)
    {
        return await QuerySingleOrDefault<ArticleOwnerDto>("SELECT id, author_id FROM articles WHERE slug = @slug", new { slug });
    }

    public async Task<long> Create(long viewer, string title, string description, string body, string[] tags)
    {
        var id = await ExecuteScalar<long>($"""
            INSERT INTO articles (id, slug, author_id, title, description, body)
            SELECT n.id, {ArticleSql.Slugify("@title")} || '-' || n.id, @viewer, @title, @description, @body
              FROM (SELECT nextval(pg_get_serial_sequence('articles', 'id')) AS id) n
            RETURNING id
            """, new { viewer, title, description, body });
        if (tags.Length > 0)
        {
            // Sorted, so two articles adding the same new tags can't deadlock.
            await Execute(
                "INSERT INTO tags (name) SELECT DISTINCT unnest(@tags::text[]) ORDER BY 1 ON CONFLICT (name) DO NOTHING",
                new { tags });
            await Execute(
                "INSERT INTO article_tags (article_id, tag_id) SELECT @id, t.id FROM tags t WHERE t.name = ANY(@tags::text[])",
                new { id, tags });
        }
        return id;
    }

    public async Task Update(long id, string? title, string? description, string? body)
    {
        await Execute($"""
            UPDATE articles SET title = COALESCE(@title, title), description = COALESCE(@description, description), body = COALESCE(@body, body),
                   slug = CASE WHEN @title::text IS NULL THEN slug ELSE {ArticleSql.Slugify("@title")} || '-' || id END,
                   updated_at = now()
             WHERE id = @id
            """, new { id, title, description, body });
    }

    public async Task Delete(long id)
    {
        await Execute("DELETE FROM articles WHERE id = @id", new { id });
    }

    public async Task Favorite(long viewer, long id)
    {
        await Execute("INSERT INTO favorites (user_id, article_id) VALUES (@viewer, @id) ON CONFLICT DO NOTHING", new { viewer, id });
    }

    public async Task Unfavorite(long viewer, long id)
    {
        await Execute("DELETE FROM favorites WHERE user_id = @viewer AND article_id = @id", new { viewer, id });
    }
}
