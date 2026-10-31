using Conduit.Articles.Queries;
using Conduit.Articles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Articles.Commands;

public static class UnfavoriteArticle
{
    public record Command(long Viewer, string Slug) : ICommand<ArticleResponse>;

    public sealed class Handler(NpgsqlDataSource data, IDispatcher dispatcher) : ICommandHandler<Command, ArticleResponse>
    {
        public async Task<Result<ArticleResponse>> Handle(Command command)
        {
            long id;
            await using (var db = await data.OpenConnectionAsync())
            {
                if (await ArticleLookup.FindId(db, command.Slug) is not { } found) return Error.NotFound("article not found");
                id = found;

                await db.ExecuteAsync("DELETE FROM favorites WHERE user_id = @Viewer AND article_id = @id", new { command.Viewer, id });
            }

            // After the connection is back in the pool: the query takes its own.
            return await dispatcher.Query(new GetArticle.ById(command.Viewer, id));
        }
    }
}
