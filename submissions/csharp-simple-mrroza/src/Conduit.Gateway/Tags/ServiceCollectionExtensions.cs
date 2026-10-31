using Conduit.Shared;
using Conduit.Tags.Integrations;

namespace Conduit.Gateway.Tags;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTagsApi(this IServiceCollection services, IConfiguration configuration) => services
        .AddTagsClient(configuration)
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGroup("/api").MapGet("/tags", async (ITagsClient tags) => Results.Ok(await tags.List()));
}
