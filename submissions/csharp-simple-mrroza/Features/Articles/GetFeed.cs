using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public sealed class GetFeed : IEndpoint
{
    public record Query(long Viewer, int Limit, int Offset);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/articles/feed", async (HttpRequest http, ClaimsPrincipal principal, IQueryHandler<Query, ArticleList> handler) =>
        {
            if (!Paging.TryRead(http, out var limit, out var offset)) return Paging.Invalid.ToHttp();
            return (await handler.Handle(new Query(principal.UserId(), limit, offset))).ToHttp(Results.Ok);
        }).AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ArticleList>
    {
        public async Task<Result<ArticleList>> Handle(Query query)
        {
            await using var db = await data.OpenConnectionAsync();
            var rows = await db.QueryAsync<ArticleRow>(ArticleSql.Feed, query);
            var articlesCount = await db.ExecuteScalarAsync<long>(ArticleSql.CountFeed, new { query.Viewer });
            return new ArticleList(rows.Select(row => row.ToArticle()), articlesCount);
        }
    }
}
