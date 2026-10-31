using Conduit.Shared;
using Conduit.Users.Integrations.Protos;

namespace Conduit.Users.Integrations;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsersClient(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<UsersRpc.UsersRpcClient>(configuration, "USERS_URL")
        .AddTransient<IUsersClient, UsersGrpcClient>();
}
