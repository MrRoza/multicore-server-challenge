using Conduit.Articles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Articles.Queries;

public static class GetArticle
{
    public record Query(long Viewer, string Slug) : IQuery<ArticleResponseDto>;

    /// <summary>What article commands read back after their write.</summary>
    public record ById(long Viewer, long Id) : IQuery<ArticleResponseDto>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ArticleResponseDto>, IQueryHandler<ById, ArticleResponseDto>
    {
        public Task<Result<ArticleResponseDto>> Handle(Query query) => Find(ArticleSql.FullSelect + " WHERE a.slug = @Slug", query);

        public Task<Result<ArticleResponseDto>> Handle(ById query) => Find(ArticleSql.FullSelect + " WHERE a.id = @Id", query);

        async Task<Result<ArticleResponseDto>> Find(string sql, object args)
        {
            await using (var db = await data.OpenConnectionAsync())
            {
                var row = await db.QuerySingleOrDefaultAsync<ArticleRowDto>(sql, args);
                if (row is null) return Error.NotFound("article not found");
                return new ArticleResponseDto(row.ToArticle());
            }
        }
    }
}
