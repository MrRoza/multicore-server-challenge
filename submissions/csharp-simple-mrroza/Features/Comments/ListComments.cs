using System.Security.Claims;
using Conduit.Features.Articles;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Comments;

public sealed class ListComments : IEndpoint
{
    public record Query(long Viewer, string Slug);

    public record Response(IEnumerable<Comment> Comments);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/articles/{slug}/comments", async (string slug, ClaimsPrincipal principal, IQueryHandler<Query, Response> handler) =>
            (await handler.Handle(new Query(principal.UserId(), slug))).ToHttp(Results.Ok))
            .AuthOptional();

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query)
        {
            await using var db = await data.OpenConnectionAsync();
            if (await ArticleLookup.FindId(db, query.Slug) is not { } articleId) return Error.NotFound("article not found");

            var rows = await db.QueryAsync<CommentRow>("""
                SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image,
                       EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @Viewer AND fo.followee_id = c.author_id) AS following
                  FROM comments c JOIN users u ON u.id = c.author_id
                 WHERE c.article_id = @articleId
                 ORDER BY c.created_at DESC, c.id DESC
                 LIMIT 100
                """, new { query.Viewer, articleId });
            return new Response(rows.Select(row => row.ToComment()));
        }
    }
}
