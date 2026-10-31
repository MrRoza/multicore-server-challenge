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
            if (await comments.ListBySlug(query.Viewer, query.Slug) is not { } list) return Error.NotFound("article not found");
            return new ListCommentsResponseDto(list);
        }
    }
}
