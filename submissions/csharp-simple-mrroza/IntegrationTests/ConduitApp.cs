using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// The real app on Kestrel, listening on a real port, against Postgres 18 in a container. It connects as the limited
/// `conduit` role with the challenge's schema, as on the benchmark box.
/// </summary>
public sealed class ConduitApp : IAsyncLifetime
{
    public const string JwtSecret = "multicore-server-challenge";

    readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("conduit")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    WebApplicationFactory<Program>? factory;
    Uri baseAddress = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await CreateSchema();

        var port = FreePort();
        var database = $"postgres://conduit:conduit@{postgres.Hostname}:{postgres.GetMappedPublicPort(5432)}/conduit";
        Environment.SetEnvironmentVariable("DATABASE_URL", database);
        Environment.SetEnvironmentVariable("JWT_SECRET", JwtSecret);
        Environment.SetEnvironmentVariable("HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("PORT", port.ToString());

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(host => host.UseEnvironment("Production"));
        factory.UseKestrel(port);
        factory.StartServer();
        baseAddress = new Uri($"http://127.0.0.1:{port}");
    }

    /// <summary>A plain HttpClient: every request goes over a socket to Kestrel.</summary>
    public ApiClient Client() => new(new HttpClient { BaseAddress = baseAddress });

    public async Task DisposeAsync()
    {
        if (factory is not null) await factory.DisposeAsync();
        await postgres.DisposeAsync();
    }

    async Task CreateSchema()
    {
        await using var connection = new NpgsqlConnection(postgres.GetConnectionString());
        await connection.OpenAsync();
        var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
        await using var command = new NpgsqlCommand($"""
            {schema}
            CREATE ROLE conduit LOGIN PASSWORD 'conduit';
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO conduit;
            GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO conduit;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
