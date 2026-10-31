using Conduit.Contracts;
using Conduit.Gateway.Tags.Responses;
using Conduit.Shared;
using Google.Protobuf.WellKnownTypes;

namespace Conduit.Gateway.Tags;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTagsApi(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<TagsRpc.TagsRpcClient>(configuration, "TAGS_URL")
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app) =>
        app.MapGroup("/api").MapGet("/tags", async (TagsRpc.TagsRpcClient tags) =>
            Results.Ok(new ListTagsResponse((await tags.ListAsync(new Empty())).Tags)));
}
