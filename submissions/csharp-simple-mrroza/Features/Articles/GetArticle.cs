using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public sealed class GetArticle : IEndpoint
{
    public record Query(long Viewer, string Slug);

    /// <summary>What article commands read back after their write.</summary>
    public record ById(long Viewer, long Id);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/articles/{slug}", async (string slug, ClaimsPrincipal principal, IQueryHandler<Query, Article> handler) =>
            (await handler.Handle(new Query(principal.UserId(), slug)))
                .ToHttp(article => Results.Ok(new ArticleResponse(article))))
            .AuthOptional();

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, Article>, IQueryHandler<ById, Article>
    {
        public Task<Result<Article>> Handle(Query query) => Find(ArticleSql.FullSelect + " WHERE a.slug = @Slug", query);

        public Task<Result<Article>> Handle(ById query) => Find(ArticleSql.FullSelect + " WHERE a.id = @Id", query);

        async Task<Result<Article>> Find(string sql, object args)
        {
            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleOrDefaultAsync<ArticleRow>(sql, args);
            if (row is null) return Error.NotFound("article not found");
            return row.ToArticle();
        }
    }
}
