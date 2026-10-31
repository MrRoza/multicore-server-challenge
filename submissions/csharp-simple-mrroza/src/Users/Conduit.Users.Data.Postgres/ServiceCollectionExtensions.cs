using Conduit.Users.Abstractions;

namespace Conduit.Users.Data.Postgres;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsersPostgres(this IServiceCollection services) =>
        services.AddScoped<IUserRepository, UserRepository>();
}
