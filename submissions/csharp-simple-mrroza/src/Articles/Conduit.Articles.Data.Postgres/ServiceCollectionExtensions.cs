using Conduit.Articles.Abstractions;
using Conduit.Shared.Data.Postgres;

namespace Conduit.Articles.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticlesPostgres(this IServiceCollection services) =>
        services.AddPostgres().AddScoped<IArticleRepository, ArticleRepository>();
}
