using Conduit.Articles.Queries;
using Conduit.Articles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Articles.Commands;

public static class UpdateArticle
{
    public record Command(long Viewer, string Slug, string? Title, string? Description, string? Body) : ICommand<ArticleResponse>;

    public sealed class Handler(NpgsqlDataSource data, IDispatcher dispatcher) : ICommandHandler<Command, ArticleResponse>
    {
        public async Task<Result<ArticleResponse>> Handle(Command command)
        {
            var fields = command;
            if (fields.Title == "" || fields.Description == "" || fields.Body == "") return Error.Validation("title, description and body can't be empty");

            await using var db = await data.OpenConnectionAsync();
            return await (await ArticleLookup.FindOwnId(db, command.Slug, command.Viewer)).Then(async id =>
            {
                await db.ExecuteAsync($"""
                    UPDATE articles SET title = COALESCE(@Title, title), description = COALESCE(@Description, description), body = COALESCE(@Body, body),
                           slug = CASE WHEN @Title::text IS NULL THEN slug ELSE {ArticleSql.Slugify("@Title")} || '-' || id END,
                           updated_at = now()
                     WHERE id = @id
                    """, new { id, fields.Title, fields.Description, fields.Body });
                return await dispatcher.Query(new GetArticle.ById(command.Viewer, id));
            });
        }
    }
}
