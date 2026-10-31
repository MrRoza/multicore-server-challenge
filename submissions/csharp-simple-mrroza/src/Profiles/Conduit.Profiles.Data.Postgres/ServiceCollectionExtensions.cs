using Conduit.Profiles.Abstractions;

namespace Conduit.Profiles.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfilesPostgres(this IServiceCollection services) =>
        services.AddScoped<IProfileRepository, ProfileRepository>();
}
