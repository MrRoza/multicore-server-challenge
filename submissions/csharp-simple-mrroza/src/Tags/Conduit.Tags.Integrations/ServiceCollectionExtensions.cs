using Conduit.Shared;
using Conduit.Tags.Integrations.Protos;

namespace Conduit.Tags.Integrations;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTagsClient(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<TagsRpc.TagsRpcClient>(configuration, "TAGS_URL")
        .AddTransient<ITagsClient, TagsGrpcClient>();
}
