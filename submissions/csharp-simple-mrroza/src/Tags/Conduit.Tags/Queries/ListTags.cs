using Conduit.Shared.Cqrs;
using Conduit.Tags.Abstractions;

namespace Conduit.Tags.Queries;

public static class ListTags
{
    public record Query : IQuery<ListTagsResponseDto>;

    public sealed class Handler(ITagRepository tags) : IQueryHandler<Query, ListTagsResponseDto>
    {
        public async Task<Result<ListTagsResponseDto>> Handle(Query query) => new ListTagsResponseDto(await tags.List());
    }
}
