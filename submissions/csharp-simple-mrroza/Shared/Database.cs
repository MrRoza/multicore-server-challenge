using Npgsql;

namespace Conduit.Shared;

public static class Database
{
    public static IServiceCollection AddDatabase(this IServiceCollection services, IConfiguration configuration)
    {
        var url = configuration["DATABASE_URL"] ?? throw new InvalidOperationException("DATABASE_URL is required");
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
        return services.AddSingleton(NpgsqlDataSource.Create(ToConnectionString(url)));
    }

    static string ToConnectionString(string url)
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
            MaxPoolSize = 90, // the challenge allows 95 connections at most
        }.ConnectionString;
    }
}
