using Conduit.Shared;
using Conduit.Shared.Auth;
using Conduit.Users.Abstractions;
using Conduit.Users.Commands;
using Conduit.Users.Queries;

namespace Conduit.Users;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsers(this IServiceCollection services) => services
        .AddScoped<IUsersClient, UsersClient>()
        .AddSingleton<TokenService>()
        .AddCommand<RegisterUser.Command, UserResponseDto, RegisterUser.Handler>()
        .AddQuery<LoginUser.Query, UserResponseDto, LoginUser.Handler>(options => options with { Enabled = false }) // Each login gets its own token
        .AddQuery<GetCurrentUser.Query, UserResponseDto, GetCurrentUser.Handler>()
        .AddCommand<UpdateUser.Command, UserResponseDto, UpdateUser.Handler>();
}
