using Conduit.Profiles.Integrations.Protos;
using Conduit.Shared;

namespace Conduit.Profiles.Integrations;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfilesClient(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<ProfilesRpc.ProfilesRpcClient>(configuration, "PROFILES_URL")
        .AddTransient<IProfilesClient, ProfilesGrpcClient>();
}
