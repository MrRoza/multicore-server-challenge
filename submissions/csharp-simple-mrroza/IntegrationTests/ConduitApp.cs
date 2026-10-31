using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// The server as its own process, listening on a real port, against Postgres 18 in a container. It connects as the
/// limited `conduit` role with the challenge's schema, as on the benchmark box.
/// </summary>
public sealed class ConduitApp : IAsyncLifetime
{
    public const string JwtSecret = "multicore-server-challenge";

    readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("conduit")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    Process? server;
    Uri baseAddress = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await CreateSchema();

        var port = FreePort();
        Start(port, $"postgres://conduit:conduit@{postgres.Hostname}:{postgres.GetMappedPublicPort(5432)}/conduit");
        baseAddress = new Uri($"http://127.0.0.1:{port}");
        await WaitForPort(port);
    }

    /// <summary>A plain HttpClient: every request goes over a socket to the server.</summary>
    public ApiClient Client() => new(new HttpClient { BaseAddress = baseAddress });

    public async Task DisposeAsync()
    {
        if (server is not null)
        {
            server.Kill(entireProcessTree: true);
            await server.WaitForExitAsync();
            server.Dispose();
        }
        await postgres.DisposeAsync();
    }

    void Start(int port, string database)
    {
        // <submission>/IntegrationTests/bin/<configuration>/net10.0/
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = output.Parent!.Name;
        var submission = output.Parent.Parent!.Parent!.Parent!.FullName;
        var folder = Path.Combine(submission, "src", "Conduit.Host", "bin", configuration, "net10.0");

        var start = new ProcessStartInfo("dotnet", "conduit.dll") { WorkingDirectory = folder, UseShellExecute = false };
        start.Environment["HOST"] = "127.0.0.1";
        start.Environment["PORT"] = port.ToString();
        start.Environment["DATABASE_URL"] = database;
        start.Environment["JWT_SECRET"] = JwtSecret;
        start.Environment["DB_POOL_SIZE"] = "20";
        server = Process.Start(start)!;
    }

    static async Task WaitForPort(int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    await client.ConnectAsync(IPAddress.Loopback, port);
                    return;
                }
            }
            catch (SocketException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(200);
            }
        }
    }

    async Task CreateSchema()
    {
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            var schema = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "schema.sql"));
            await using (var command = new NpgsqlCommand($"""
                {schema}
                CREATE ROLE conduit LOGIN PASSWORD 'conduit';
                GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO conduit;
                GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO conduit;
                """, connection))
            {
                await command.ExecuteNonQueryAsync();
            }
        }
    }

    static int FreePort()
    {
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
    }
}
