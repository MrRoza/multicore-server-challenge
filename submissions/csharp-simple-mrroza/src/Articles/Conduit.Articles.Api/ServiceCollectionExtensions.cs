using Conduit.Shared;

namespace Conduit.Articles.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticlesApi(this IServiceCollection services) =>
        services.AddEndpoints(endpoints => endpoints.MapGrpcService<ArticlesGrpcService>());
}
