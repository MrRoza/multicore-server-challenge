using Conduit.Shared.Data.Postgres;
using Conduit.Users.Abstractions;

namespace Conduit.Users.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsersPostgres(this IServiceCollection services) =>
        services.AddPostgres().AddScoped<IUserRepository, UserRepository>();
}
