using Conduit.Comments.Abstractions;
using Conduit.Shared.Data;
using Dapper;

namespace Conduit.Comments.Data.Postgres;

public sealed class CommentRepository(IUnitOfWork work) : ICommentRepository
{
    public async Task<long?> FindArticleId(string slug)
    {
        return await work.Connection.QuerySingleOrDefaultAsync<long?>("SELECT id FROM articles WHERE slug = @slug", new { slug });
    }

    public async Task<IEnumerable<CommentDto>> List(long viewer, long articleId)
    {
        var rows = await work.Connection.QueryAsync<CommentRowDto>("""
            SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image,
                   EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @viewer AND fo.followee_id = c.author_id) AS following
              FROM comments c JOIN users u ON u.id = c.author_id
             WHERE c.article_id = @articleId
             ORDER BY c.created_at DESC, c.id DESC
             LIMIT 100
            """, new { viewer, articleId });
        return rows.Select(row => row.ToComment());
    }

    public async Task<CommentDto> Add(long viewer, long articleId, string body)
    {
        var row = await work.Connection.QuerySingleAsync<CommentRowDto>("""
            WITH c AS (INSERT INTO comments (article_id, author_id, body) VALUES (@articleId, @viewer, @body)
                       RETURNING id, body, created_at, updated_at, author_id)
            SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image
              FROM c JOIN users u ON u.id = c.author_id
            """, new { articleId, viewer, body });
        return row.ToComment();
    }

    public async Task<long?> FindAuthorId(long id, string slug)
    {
        return await work.Connection.QuerySingleOrDefaultAsync<long?>("""
            SELECT c.author_id FROM comments c JOIN articles a ON a.id = c.article_id
             WHERE c.id = @id AND a.slug = @slug
            """, new { id, slug });
    }

    public async Task Delete(long id)
    {
        await work.Connection.ExecuteAsync("DELETE FROM comments WHERE id = @id", new { id });
    }
}
