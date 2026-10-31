using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Conduit.Shared.Data.Postgres;

public static class ServiceCollectionExtensions
{
    /// <summary>The connection pool and the unit of work. Each slice's repositories need them.</summary>
    public static IServiceCollection AddPostgres(this IServiceCollection services)
    {
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        services.TryAddSingleton(provider =>
        {
            var configuration = provider.GetRequiredService<IConfiguration>();
            var url = configuration["DATABASE_URL"] ?? throw new InvalidOperationException("DATABASE_URL is required");
            return NpgsqlDataSource.Create(ToConnectionString(url, configuration));
        });
        services.TryAddScoped<PostgresUnitOfWork>();
        services.TryAddScoped<IUnitOfWork>(provider => provider.GetRequiredService<PostgresUnitOfWork>());
        return services;
    }

    static string ToConnectionString(string url, IConfiguration configuration)
    {
        var uri = new Uri(url);
        var login = uri.UserInfo.Split(':', 2);
        return new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Username = Uri.UnescapeDataString(login[0]),
            Password = login.Length > 1 ? Uri.UnescapeDataString(login[1]) : null,
            Database = uri.AbsolutePath.TrimStart('/'),
            MaxPoolSize = configuration.GetValue("DB_POOL_SIZE", 20), // start.sh splits the challenge's 95 connections between the services
        }.ConnectionString;
    }
}
