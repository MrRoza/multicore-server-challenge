using Conduit.Shared;

namespace Conduit.Profiles.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfilesApi(this IServiceCollection services) =>
        services.AddEndpoints(endpoints => endpoints.MapGrpcService<ProfilesGrpcService>());
}
