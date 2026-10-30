using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public sealed class UpdateArticle : IEndpoint
{
    public record Fields(string? Title, string? Description, string? Body);

    public record Request(Fields? Article);

    public record Command(long Viewer, string Slug, Fields? Changes);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapPut("/articles/{slug}", async (string slug, Request request, ClaimsPrincipal principal,
            ICommandHandler<Command, long> handler, IQueryHandler<GetArticle.ById, Article> getArticle) =>
            (await handler.Handle(new Command(principal.UserId(), slug, request.Article))
                .Then(id => getArticle.Handle(new GetArticle.ById(principal.UserId(), id))))
                .ToHttp(article => Results.Ok(new ArticleResponse(article))))
            .AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, long>
    {
        public async Task<Result<long>> Handle(Command command)
        {
            if (command.Changes is not { } fields) return Error.Validation("article is required");
            if (fields.Title == "" || fields.Description == "" || fields.Body == "") return Error.Validation("title, description and body can't be empty");

            await using var db = await data.OpenConnectionAsync();
            return await (await ArticleLookup.FindOwnId(db, command.Slug, command.Viewer)).Then<long>(async id =>
            {
                await db.ExecuteAsync($"""
                    UPDATE articles SET title = COALESCE(@Title, title), description = COALESCE(@Description, description), body = COALESCE(@Body, body),
                           slug = CASE WHEN @Title::text IS NULL THEN slug ELSE {ArticleSql.Slugify("@Title")} || '-' || id END,
                           updated_at = now()
                     WHERE id = @id
                    """, new { id, fields.Title, fields.Description, fields.Body });
                return id;
            });
        }
    }
}
