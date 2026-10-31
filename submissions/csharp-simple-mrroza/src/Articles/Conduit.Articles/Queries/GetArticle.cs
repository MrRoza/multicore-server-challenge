using Conduit.Articles.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles.Queries;

public static class GetArticle
{
    public record Query(long Viewer, string Slug) : IQuery<ArticleResponseDto>;

    /// <summary>What article commands read back after their write.</summary>
    public record ById(long Viewer, long Id) : IQuery<ArticleResponseDto>;

    public sealed class Handler(IArticleRepository articles) : IQueryHandler<Query, ArticleResponseDto>, IQueryHandler<ById, ArticleResponseDto>
    {
        public async Task<Result<ArticleResponseDto>> Handle(Query query) => ToResult(await articles.FindBySlug(query.Viewer, query.Slug));

        public async Task<Result<ArticleResponseDto>> Handle(ById query) => ToResult(await articles.FindById(query.Viewer, query.Id));

        static Result<ArticleResponseDto> ToResult(ArticleDto? article)
        {
            if (article is null) return Error.NotFound("article not found");
            return new ArticleResponseDto(article);
        }
    }
}
