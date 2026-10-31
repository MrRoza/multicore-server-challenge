using Conduit.Shared;

namespace Conduit.Tags.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTagsApi(this IServiceCollection services) =>
        services.AddEndpoints(endpoints => endpoints.MapGrpcService<TagsGrpcService>());
}
