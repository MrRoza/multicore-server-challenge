using Conduit.Articles.Queries;
using Conduit.Articles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Articles.Commands;

public static class CreateArticle
{
    public record Command(long Viewer, string? Title, string? Description, string? Body, string[] TagList) : ICommand<ArticleResponse>;

    public sealed class Handler(NpgsqlDataSource data, IDispatcher dispatcher) : ICommandHandler<Command, ArticleResponse>
    {
        public async Task<Result<ArticleResponse>> Handle(Command command)
        {
            var fields = command;
            if (string.IsNullOrEmpty(fields.Title) || string.IsNullOrEmpty(fields.Description) || string.IsNullOrEmpty(fields.Body))
                return Error.Validation("title, description and body are required");
            var tags = fields.TagList;
            if (tags.Any(string.IsNullOrEmpty)) return Error.Validation("tags can't be empty");

            long id;
            await using (var db = await data.OpenConnectionAsync())
            {
                await using (var transaction = await db.BeginTransactionAsync())
                {
                    id = await db.ExecuteScalarAsync<long>($"""
                        INSERT INTO articles (id, slug, author_id, title, description, body)
                        SELECT n.id, {ArticleSql.Slugify("@Title")} || '-' || n.id, @Viewer, @Title, @Description, @Body
                          FROM (SELECT nextval(pg_get_serial_sequence('articles', 'id')) AS id) n
                        RETURNING id
                        """, new { command.Viewer, fields.Title, fields.Description, fields.Body }, transaction);
                    if (tags.Length > 0)
                    {
                        // Sorted, so two articles adding the same new tags can't deadlock.
                        await db.ExecuteAsync(
                            "INSERT INTO tags (name) SELECT DISTINCT unnest(@tags::text[]) ORDER BY 1 ON CONFLICT (name) DO NOTHING",
                            new { tags }, transaction);
                        await db.ExecuteAsync(
                            "INSERT INTO article_tags (article_id, tag_id) SELECT @id, t.id FROM tags t WHERE t.name = ANY(@tags::text[])",
                            new { id, tags }, transaction);
                    }
                    await transaction.CommitAsync();
                }
            }

            // After the connection is back in the pool: the query takes its own.
            return await dispatcher.Query(new GetArticle.ById(command.Viewer, id));
        }
    }
}
