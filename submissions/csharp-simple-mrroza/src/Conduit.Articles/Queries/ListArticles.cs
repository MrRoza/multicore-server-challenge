using Conduit.Articles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Articles.Queries;

public static class ListArticles
{
    public record Query(long Viewer, string? Limit, string? Offset, string? Tag, string? Author, string? Favorited) : IQuery<ArticleListResponse>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ArticleListResponse>
    {
        public async Task<Result<ArticleListResponse>> Handle(Query query)
        {
            if (!Paging.TryParse(query.Limit, query.Offset, out var limit, out var offset)) return Paging.Invalid;

            // Only the first filter given applies, in this order. An empty one counts as not given.
            var (page, count, filter) =
                !string.IsNullOrEmpty(query.Tag) ? (ArticleSql.ByTag, ArticleSql.CountByTag, query.Tag)
                : !string.IsNullOrEmpty(query.Author) ? (ArticleSql.ByAuthor, ArticleSql.CountByAuthor, query.Author)
                : !string.IsNullOrEmpty(query.Favorited) ? (ArticleSql.ByFavorited, ArticleSql.CountByFavorited, query.Favorited)
                : (ArticleSql.All, ArticleSql.CountAll, null);

            await using var db = await data.OpenConnectionAsync();
            var rows = await db.QueryAsync<ArticleRow>(page, new { query.Viewer, Limit = limit, Offset = offset, filter });
            var articlesCount = await db.ExecuteScalarAsync<long>(count, new { filter });
            return new ArticleListResponse(rows.Select(row => row.ToArticle()), articlesCount);
        }
    }
}
