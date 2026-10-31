using Conduit.Profiles.Abstractions;
using Conduit.Shared.Data.Postgres;

namespace Conduit.Profiles.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfilesPostgres(this IServiceCollection services) =>
        services.AddPostgres().AddScoped<IProfileRepository, ProfileRepository>();
}
