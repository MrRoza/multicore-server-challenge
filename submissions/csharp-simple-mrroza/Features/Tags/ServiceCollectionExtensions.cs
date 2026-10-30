using Conduit.Features.Tags.Queries;
using Conduit.Features.Tags.Responses;
using Conduit.Shared;
using Conduit.Shared.Cqrs;

namespace Conduit.Features.Tags;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTags(this IServiceCollection services) => services
        .AddQuery<ListTags.Query, ListTagsResponse, ListTags.Handler>()
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder api) =>
        api.MapGet("/tags", async (IDispatcher dispatcher) => (await dispatcher.Query(new ListTags.Query())).ToHttp());
}
