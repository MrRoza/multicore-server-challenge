using Conduit.Articles.Abstractions;

namespace Conduit.Articles.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticlesPostgres(this IServiceCollection services) =>
        services.AddScoped<IArticleRepository, ArticleRepository>();
}
