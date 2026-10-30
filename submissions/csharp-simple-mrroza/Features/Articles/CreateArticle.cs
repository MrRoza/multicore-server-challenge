using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public sealed class CreateArticle : IEndpoint
{
    public record Fields(string? Title, string? Description, string? Body, string?[]? TagList);

    public record Request(Fields? Article);

    public record Command(long Viewer, Fields? Article);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapPost("/articles", async (Request request, ClaimsPrincipal principal,
            ICommandHandler<Command, long> handler, IQueryHandler<GetArticle.ById, Article> getArticle) =>
            (await handler.Handle(new Command(principal.UserId(), request.Article))
                .Then(id => getArticle.Handle(new GetArticle.ById(principal.UserId(), id))))
                .ToHttp(article => Results.Json(new ArticleResponse(article), statusCode: StatusCodes.Status201Created)))
            .AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, long>
    {
        public async Task<Result<long>> Handle(Command command)
        {
            var fields = command.Article;
            if (fields is null || string.IsNullOrEmpty(fields.Title) || string.IsNullOrEmpty(fields.Description) || string.IsNullOrEmpty(fields.Body))
                return Error.Validation("title, description and body are required");
            var tags = fields.TagList ?? [];
            if (tags.Any(string.IsNullOrEmpty)) return Error.Validation("tags can't be empty");

            await using var db = await data.OpenConnectionAsync();
            await using var transaction = await db.BeginTransactionAsync();
            var id = await db.ExecuteScalarAsync<long>($"""
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
            return id;
        }
    }
}
