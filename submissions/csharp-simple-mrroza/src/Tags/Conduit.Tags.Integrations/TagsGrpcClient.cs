using Conduit.Tags.Abstractions;
using Conduit.Tags.Integrations.Protos;
using Google.Protobuf.WellKnownTypes;

namespace Conduit.Tags.Integrations;

public sealed class TagsGrpcClient(TagsRpc.TagsRpcClient rpc) : ITagsClient
{
    public async Task<ListTagsResponseDto> List() => new((await rpc.ListAsync(new Empty())).Tags);
}
