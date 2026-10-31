using Conduit.Comments.Abstractions;
using Conduit.Shared.Data.Postgres;

namespace Conduit.Comments.Data.Postgres;

public sealed class CommentRepository(PostgresUnitOfWork work) : PostgresRepository(work), ICommentRepository
{
    public async Task<long?> FindArticleId(string slug)
    {
        return await QuerySingleOrDefault<long?>("SELECT id FROM articles WHERE slug = @slug", new { slug });
    }

    public async Task<IEnumerable<CommentDto>?> ListBySlug(long viewer, string slug)
    {
        var (ids, rows) = await QueryBoth<long, CommentRowDto>("SELECT id FROM articles WHERE slug = @slug", """
            SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image,
                   EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @viewer AND fo.followee_id = c.author_id) AS following
              FROM comments c JOIN users u ON u.id = c.author_id
             WHERE c.article_id = (SELECT a.id FROM articles a WHERE a.slug = @slug)
             ORDER BY c.created_at DESC, c.id DESC
             LIMIT 100
            """, new { viewer, slug });
        return ids.Any() ? rows.Select(row => row.ToComment()) : null;
    }

    public async Task<CommentDto> Add(long viewer, long articleId, string body)
    {
        var row = await QuerySingle<CommentRowDto>("""
            WITH c AS (INSERT INTO comments (article_id, author_id, body) VALUES (@articleId, @viewer, @body)
                       RETURNING id, body, created_at, updated_at, author_id)
            SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image
              FROM c JOIN users u ON u.id = c.author_id
            """, new { articleId, viewer, body });
        return row.ToComment();
    }

    public async Task<long?> FindAuthorId(long id, string slug)
    {
        return await QuerySingleOrDefault<long?>("""
            SELECT c.author_id FROM comments c JOIN articles a ON a.id = c.article_id
             WHERE c.id = @id AND a.slug = @slug
            """, new { id, slug });
    }

    public async Task Delete(long id)
    {
        await Execute("DELETE FROM comments WHERE id = @id", new { id });
    }
}
