using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;
using Conduit.Tags.Abstractions;
using Conduit.Tags.Queries;

namespace Conduit.Tags;

/// <summary>Uses the slice in the same process: one unit of work per call, and no network.</summary>
public sealed class TagsClient(IDispatcher dispatcher, IUnitOfWork work) : ITagsClient
{
    public async Task<ListTagsResponseDto> List()
    {
        await using (work.Begin())
        {
            return (await dispatcher.Query(new ListTags.Query())).Unwrap();
        }
    }
}
