using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;
using Conduit.Tags.Integrations.Protos;
using Conduit.Tags.Queries;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Conduit.Tags.Api;

public sealed class TagsGrpcService(IDispatcher dispatcher, IUnitOfWork work) : TagsRpc.TagsRpcBase
{
    public override async Task<TagListMessage> List(Empty request, ServerCallContext context)
    {
        await using (await work.Begin())
        {
            return new() { Tags = { (await dispatcher.Query(new ListTags.Query())).Unwrap().Tags } };
        }
    }
}
