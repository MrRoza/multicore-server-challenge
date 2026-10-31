using Conduit.Tags.Abstractions;

namespace Conduit.Tags.Integrations;

/// <summary>Talks to the Tags service. Its failures arrive as RpcException.</summary>
public interface ITagsClient
{
    Task<ListTagsResponseDto> List();
}
