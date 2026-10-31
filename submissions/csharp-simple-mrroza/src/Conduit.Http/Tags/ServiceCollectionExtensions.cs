using Conduit.Shared;
using Conduit.Tags.Abstractions;

namespace Conduit.Http.Tags;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTagsApi(this IServiceCollection services) =>
        services.AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGroup("/api").MapGet("/tags", async (ITagsClient tags) => Results.Ok(await tags.List()));
}
