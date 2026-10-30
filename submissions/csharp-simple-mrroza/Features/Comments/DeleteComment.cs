using System.Globalization;
using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Comments;

public sealed class DeleteComment : IEndpoint
{
    public record Command(long Viewer, string Slug, string Id);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapDelete("/articles/{slug}/comments/{id}", async (string slug, string id, ClaimsPrincipal principal, ICommandHandler<Command, Unit> handler) =>
            (await handler.Handle(new Command(principal.UserId(), slug, id))).ToHttp(_ => Results.NoContent()))
            .AuthRequired();

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
