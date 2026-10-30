using System.Security.Claims;
using Conduit.Features.Articles;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Comments;

public sealed class AddComment : IEndpoint
{
    public record Fields(string? Body);

    public record Request(Fields? Comment);

    public record Command(long Viewer, string Slug, string? Body);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/articles/{slug}/comments", async (string slug, Request request, ClaimsPrincipal principal, ICommandHandler<Command, Comment> handler) =>
            (await handler.Handle(new Command(principal.UserId(), slug, request.Comment?.Body)))
                .ToHttp(comment => Results.Json(new CommentResponse(comment), statusCode: StatusCodes.Status201Created)))
            .AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, Comment>
    {
        public async Task<Result<Comment>> Handle(Command command)
        {
            if (string.IsNullOrEmpty(command.Body)) return Error.Validation("body is required");

            await using var db = await data.OpenConnectionAsync();
            if (await ArticleLookup.FindId(db, command.Slug) is not { } articleId) return Error.NotFound("article not found");

            var row = await db.QuerySingleAsync<CommentRow>("""
                WITH c AS (INSERT INTO comments (article_id, author_id, body) VALUES (@articleId, @Viewer, @Body)
                           RETURNING id, body, created_at, updated_at, author_id)
                SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image
                  FROM c JOIN users u ON u.id = c.author_id
                """, new { articleId, command.Viewer, command.Body });
            return row.ToComment();
        }
    }
}
