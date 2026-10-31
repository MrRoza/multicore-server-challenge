using Conduit.Shared;

namespace Conduit.Users.Api;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsersApi(this IServiceCollection services) =>
        services.AddEndpoints(endpoints => endpoints.MapGrpcService<UsersGrpcService>());
}
