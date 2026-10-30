using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles;

public sealed class ListArticles : IEndpoint
{
    public record Query(long Viewer, int Limit, int Offset, string? Tag, string? Author, string? Favorited);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/articles", async (HttpRequest http, ClaimsPrincipal principal, IQueryHandler<Query, ArticleList> handler) =>
        {
            if (!Paging.TryRead(http, out var limit, out var offset)) return Paging.Invalid.ToHttp();
            var query = new Query(principal.UserId(), limit, offset, http.Query["tag"], http.Query["author"], http.Query["favorited"]);
            return (await handler.Handle(query)).ToHttp(Results.Ok);
        }).AuthOptional();

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ArticleList>
    {
        public async Task<Result<ArticleList>> Handle(Query query)
        {
            // Only the first filter given applies, in this order. An empty one counts as not given.
            var (page, count, filter) =
                !string.IsNullOrEmpty(query.Tag) ? (ArticleSql.ByTag, ArticleSql.CountByTag, query.Tag)
                : !string.IsNullOrEmpty(query.Author) ? (ArticleSql.ByAuthor, ArticleSql.CountByAuthor, query.Author)
                : !string.IsNullOrEmpty(query.Favorited) ? (ArticleSql.ByFavorited, ArticleSql.CountByFavorited, query.Favorited)
                : (ArticleSql.All, ArticleSql.CountAll, null);

            await using var db = await data.OpenConnectionAsync();
            var rows = await db.QueryAsync<ArticleRow>(page, new { query.Viewer, query.Limit, query.Offset, filter });
            var articlesCount = await db.ExecuteScalarAsync<long>(count, new { filter });
            return new ArticleList(rows.Select(row => row.ToArticle()), articlesCount);
        }
    }
}
