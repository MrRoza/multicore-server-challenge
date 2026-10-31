using Conduit.Comments.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Comments.Queries;

public static class ListComments
{
    public record Query(long Viewer, string Slug) : IQuery<ListCommentsResponseDto>;

    public sealed class Handler(ICommentRepository comments) : IQueryHandler<Query, ListCommentsResponseDto>
    {
        public async Task<Result<ListCommentsResponseDto>> Handle(Query query)
        {
            if (await comments.FindArticleId(query.Slug) is not { } articleId) return Error.NotFound("article not found");
            return new ListCommentsResponseDto(await comments.List(query.Viewer, articleId));
        }
    }
}
