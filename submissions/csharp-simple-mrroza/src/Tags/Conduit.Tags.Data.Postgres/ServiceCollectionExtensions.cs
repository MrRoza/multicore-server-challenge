using Conduit.Shared.Data.Postgres;
using Conduit.Tags.Abstractions;

namespace Conduit.Tags.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTagsPostgres(this IServiceCollection services) =>
        services.AddPostgres().AddScoped<ITagRepository, TagRepository>();
}
