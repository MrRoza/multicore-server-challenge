using Conduit.Articles.Integrations.Protos;
using Conduit.Shared;

namespace Conduit.Articles.Integrations;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticlesClient(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<ArticlesRpc.ArticlesRpcClient>(configuration, "ARTICLES_URL")
        .AddTransient<IArticlesClient, ArticlesGrpcClient>();
}
