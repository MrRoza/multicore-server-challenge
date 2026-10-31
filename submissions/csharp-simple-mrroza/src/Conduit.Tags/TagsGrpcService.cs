using Conduit.Contracts;
using Conduit.Shared.Cqrs;
using Conduit.Tags.Queries;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Conduit.Tags;

public sealed class TagsGrpcService(IDispatcher dispatcher) : TagsRpc.TagsRpcBase
{
    public override async Task<TagListMessage> List(Empty request, ServerCallContext context) =>
        new() { Tags = { (await dispatcher.Query(new ListTags.Query())).Unwrap().Tags } };
}
