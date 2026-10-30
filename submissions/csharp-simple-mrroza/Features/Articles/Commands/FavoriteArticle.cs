using Conduit.Features.Articles.Queries;
using Conduit.Features.Articles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles.Commands;

public static class FavoriteArticle
{
    public record Command(long Viewer, string Slug) : ICommand<ArticleResponse>;

    public sealed class Handler(NpgsqlDataSource data, IDispatcher dispatcher) : ICommandHandler<Command, ArticleResponse>
    {
        public async Task<Result<ArticleResponse>> Handle(Command command)
        {
            await using var db = await data.OpenConnectionAsync();
            if (await ArticleLookup.FindId(db, command.Slug) is not { } id) return Error.NotFound("article not found");

            await db.ExecuteAsync(
                "INSERT INTO favorites (user_id, article_id) VALUES (@Viewer, @id) ON CONFLICT DO NOTHING",
                new { command.Viewer, id });
            return await dispatcher.Query(new GetArticle.ById(command.Viewer, id));
        }
    }
}
