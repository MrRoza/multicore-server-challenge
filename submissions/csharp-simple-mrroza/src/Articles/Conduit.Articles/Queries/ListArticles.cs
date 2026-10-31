using Conduit.Articles.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles.Queries;

public static class ListArticles
{
    public record Query(long Viewer, string? Limit, string? Offset, string? Tag, string? Author, string? Favorited) : IQuery<ArticleListResponseDto>;

    public sealed class Handler(IArticleRepository articles) : IQueryHandler<Query, ArticleListResponseDto>
    {
        public async Task<Result<ArticleListResponseDto>> Handle(Query query)
        {
            if (!Page.TryParse(query.Limit, query.Offset, out var page)) return Error.Validation(Page.InvalidMessage);
            return await articles.List(query.Viewer, page, query.Tag, query.Author, query.Favorited);
        }
    }
}
