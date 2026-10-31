using Conduit.Comments.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Comments.Commands;

public static class AddComment
{
    public record Command(long Viewer, string Slug, string? Body) : ICommand<CommentResponseDto>;

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, CommentResponseDto>
    {
        public async Task<Result<CommentResponseDto>> Handle(Command command)
        {
            if (string.IsNullOrEmpty(command.Body)) return Error.Validation("body is required");

            await using (var db = await data.OpenConnectionAsync())
            {
                if (await ArticleIds.Find(db, command.Slug) is not { } articleId) return Error.NotFound("article not found");

                var row = await db.QuerySingleAsync<CommentRowDto>("""
                    WITH c AS (INSERT INTO comments (article_id, author_id, body) VALUES (@articleId, @Viewer, @Body)
                               RETURNING id, body, created_at, updated_at, author_id)
                    SELECT c.id, c.body, c.created_at, c.updated_at, u.username, u.bio, u.image
                      FROM c JOIN users u ON u.id = c.author_id
                    """, new { articleId, command.Viewer, command.Body });
                return new CommentResponseDto(row.ToComment());
            }
        }
    }
}
