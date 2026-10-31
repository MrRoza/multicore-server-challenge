using Conduit.Comments.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Comments.Queries;

public static class ListComments
{
    public record Query(long Viewer, string Slug) : IQuery<ListCommentsResponseDto>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ListCommentsResponseDto>
    {
        public async Task<Result<ListCommentsResponseDto>> Handle(Query query)
        {
            await using (var db = await data.OpenConnectionAsync())
            {
                if (await ArticleIds.Find(db, query.Slug) is not { } articleId) return Error.NotFound("article not found");

                var rows = await db.QueryAsync<CommentRowDto>("""
                    SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image,
                           EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @Viewer AND fo.followee_id = c.author_id) AS following
                      FROM comments c JOIN users u ON u.id = c.author_id
                     WHERE c.article_id = @articleId
                     ORDER BY c.created_at DESC, c.id DESC
                     LIMIT 100
                    """, new { query.Viewer, articleId });
                return new ListCommentsResponseDto(rows.Select(row => row.ToComment()));
            }
        }
    }
}
