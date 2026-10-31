using Conduit.Articles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Articles.Queries;

public static class GetFeed
{
    public record Query(long Viewer, string? Limit, string? Offset) : IQuery<ArticleListResponseDto>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ArticleListResponseDto>
    {
        public async Task<Result<ArticleListResponseDto>> Handle(Query query)
        {
            if (!Paging.TryParse(query.Limit, query.Offset, out var limit, out var offset)) return Paging.Invalid;

            await using (var db = await data.OpenConnectionAsync())
            {
                var rows = await db.QueryAsync<ArticleRowDto>(ArticleSql.Feed, new { query.Viewer, Limit = limit, Offset = offset });
                var articlesCount = await db.ExecuteScalarAsync<long>(ArticleSql.CountFeed, new { query.Viewer });
                return new ArticleListResponseDto(rows.Select(row => row.ToArticle()), articlesCount);
            }
        }
    }
}
