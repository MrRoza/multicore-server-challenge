using Conduit.Shared;

namespace Conduit.Comments.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommentsApi(this IServiceCollection services) =>
        services.AddEndpoints(endpoints => endpoints.MapGrpcService<CommentsGrpcService>());
}
