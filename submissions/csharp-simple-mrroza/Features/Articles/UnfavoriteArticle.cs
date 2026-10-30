using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public sealed class UnfavoriteArticle : IEndpoint
{
    public record Command(long Viewer, string Slug);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapDelete("/articles/{slug}/favorite", async (string slug, ClaimsPrincipal principal,
            ICommandHandler<Command, long> handler, IQueryHandler<GetArticle.ById, Article> getArticle) =>
            (await handler.Handle(new Command(principal.UserId(), slug))
                .Then(id => getArticle.Handle(new GetArticle.ById(principal.UserId(), id))))
                .ToHttp(article => Results.Ok(new ArticleResponse(article))))
            .AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, long>
    {
        public async Task<Result<long>> Handle(Command command)
        {
            await using var db = await data.OpenConnectionAsync();
            if (await ArticleLookup.FindId(db, command.Slug) is not { } id) return Error.NotFound("article not found");

            await db.ExecuteAsync("DELETE FROM favorites WHERE user_id = @Viewer AND article_id = @id", new { command.Viewer, id });
            return id;
        }
    }
}
