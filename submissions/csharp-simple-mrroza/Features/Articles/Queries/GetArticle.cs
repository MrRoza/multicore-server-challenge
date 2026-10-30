using Conduit.Features.Articles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Features.Articles.Queries;

public static class GetArticle
{
    public record Query(long Viewer, string Slug) : IQuery<ArticleResponse>;

    /// <summary>What article commands read back after their write.</summary>
    public record ById(long Viewer, long Id) : IQuery<ArticleResponse>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ArticleResponse>, IQueryHandler<ById, ArticleResponse>
    {
        public Task<Result<ArticleResponse>> Handle(Query query) => Find(ArticleSql.FullSelect + " WHERE a.slug = @Slug", query);

        public Task<Result<ArticleResponse>> Handle(ById query) => Find(ArticleSql.FullSelect + " WHERE a.id = @Id", query);

        async Task<Result<ArticleResponse>> Find(string sql, object args)
        {
            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleOrDefaultAsync<ArticleRow>(sql, args);
            if (row is null) return Error.NotFound("article not found");
            return new ArticleResponse(row.ToArticle());
        }
    }
}
