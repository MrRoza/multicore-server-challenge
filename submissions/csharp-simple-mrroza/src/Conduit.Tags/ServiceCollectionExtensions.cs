using Conduit.Shared;
using Conduit.Tags.Queries;
using Conduit.Tags.Responses;

namespace Conduit.Tags;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTags(this IServiceCollection services) => services
        .AddQuery<ListTags.Query, ListTagsResponseDto, ListTags.Handler>()
        .AddEndpoints(endpoints => endpoints.MapGrpcService<TagsGrpcService>());
}
