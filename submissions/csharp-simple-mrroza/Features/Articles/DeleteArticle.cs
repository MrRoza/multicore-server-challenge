using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public sealed class DeleteArticle : IEndpoint
{
    public record Command(long Viewer, string Slug);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapDelete("/articles/{slug}", async (string slug, ClaimsPrincipal principal, ICommandHandler<Command, Unit> handler) =>
            (await handler.Handle(new Command(principal.UserId(), slug))).ToHttp(_ => Results.NoContent()))
            .AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, Unit>
    {
        public async Task<Result<Unit>> Handle(Command command)
        {
            await using var db = await data.OpenConnectionAsync();
            return await (await ArticleLookup.FindOwnId(db, command.Slug, command.Viewer)).Then<Unit>(async id =>
            {
                await db.ExecuteAsync("DELETE FROM articles WHERE id = @id", new { id });
                return new Unit();
            });
        }
    }
}
