using Conduit.Shared;

namespace Conduit.Features.Tags;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTags(this IServiceCollection services) => services
        .AddQuery<ListTags.Query, ListTags.Response, ListTags.Handler>()
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder api) =>
        api.MapGet("/tags", async (IDispatcher dispatcher) => (await dispatcher.Query(new ListTags.Query())).ToHttp());
}
