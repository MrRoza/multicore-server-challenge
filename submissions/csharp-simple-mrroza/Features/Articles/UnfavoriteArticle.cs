using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public static class UnfavoriteArticle
{
    public record Command(long Viewer, string Slug) : ICommand<ArticleResponse>;

    public sealed class Handler(NpgsqlDataSource data, IDispatcher dispatcher) : ICommandHandler<Command, ArticleResponse>
    {
        public async Task<Result<ArticleResponse>> Handle(Command command)
        {
            await using var db = await data.OpenConnectionAsync();
            if (await ArticleLookup.FindId(db, command.Slug) is not { } id) return Error.NotFound("article not found");

            await db.ExecuteAsync("DELETE FROM favorites WHERE user_id = @Viewer AND article_id = @id", new { command.Viewer, id });
            return await dispatcher.Query(new GetArticle.ById(command.Viewer, id));
        }
    }
}
