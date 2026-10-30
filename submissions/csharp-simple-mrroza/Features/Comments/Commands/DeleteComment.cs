using System.Globalization;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Features.Comments.Commands;

public static class DeleteComment
{
    public record Command(long Viewer, string Slug, string Id) : ICommand<Unit>;

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, Unit>
    {
        public async Task<Result<Unit>> Handle(Command command)
        {
            if (!long.TryParse(command.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return Error.NotFound("comment not found");

            await using var db = await data.OpenConnectionAsync();
            var authorId = await db.QuerySingleOrDefaultAsync<long?>("""
                SELECT c.author_id FROM comments c JOIN articles a ON a.id = c.article_id
                 WHERE c.id = @id AND a.slug = @Slug
                """, new { id, command.Slug });
            if (authorId is null) return Error.NotFound("comment not found");
            if (authorId != command.Viewer) return Error.Forbidden("only the author can do this");

            await db.ExecuteAsync("DELETE FROM comments WHERE id = @id", new { id });
            return new Unit();
        }
    }
}
