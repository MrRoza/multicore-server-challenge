using Conduit.Shared;
using Conduit.Shared.Auth;
using Conduit.Users.Commands;
using Conduit.Users.Queries;
using Conduit.Users.Responses;

namespace Conduit.Users;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsers(this IServiceCollection services) => services
        .AddSingleton<TokenService>()
        .AddCommand<RegisterUser.Command, UserResponse, RegisterUser.Handler>()
        .AddQuery<LoginUser.Query, UserResponse, LoginUser.Handler>()
        .AddQuery<GetCurrentUser.Query, UserResponse, GetCurrentUser.Handler>()
        .AddCommand<UpdateUser.Command, UserResponse, UpdateUser.Handler>()
        .AddEndpoints(endpoints => endpoints.MapGrpcService<UsersGrpcService>());
}
