using Conduit.Articles.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles.Queries;

public static class GetFeed
{
    public record Query(long Viewer, string? Limit, string? Offset) : IQuery<ArticleListResponseDto>;

    public sealed class Handler(IArticleRepository articles) : IQueryHandler<Query, ArticleListResponseDto>
    {
        public async Task<Result<ArticleListResponseDto>> Handle(Query query)
        {
            if (!Page.TryParse(query.Limit, query.Offset, out var page)) return Error.Validation(Page.InvalidMessage);
            return await articles.Feed(query.Viewer, page);
        }
    }
}
