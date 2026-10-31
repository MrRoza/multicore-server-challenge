using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Every service as its own process, listening on real ports, against Postgres 18 in a container. Requests go over HTTP
/// to the gateway and over gRPC to the slices, which connect as the limited `conduit` role, as on the benchmark box.
/// </summary>
public sealed class ConduitApp : IAsyncLifetime
{
    public const string JwtSecret = "multicore-server-challenge";

    static readonly string[] Slices = ["Users", "Profiles", "Articles", "Comments", "Tags"];

    readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:18")
        .WithDatabase("conduit")
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    readonly List<Process> services = [];
    Uri baseAddress = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        await CreateSchema();

        var database = $"postgres://conduit:conduit@{postgres.Hostname}:{postgres.GetMappedPublicPort(5432)}/conduit";
        var ports = new List<int>();
        var gatewaySettings = new Dictionary<string, string> { ["JWT_SECRET"] = JwtSecret };
        foreach (var slice in Slices)
        {
            var port = FreePort();
            ports.Add(port);
            gatewaySettings[$"{slice.ToUpperInvariant()}_URL"] = $"http://127.0.0.1:{port}";
            Start(slice, port, new Dictionary<string, string> { ["DATABASE_URL"] = database, ["JWT_SECRET"] = JwtSecret, ["DB_POOL_SIZE"] = "10" });
        }

        var gatewayPort = FreePort();
        ports.Add(gatewayPort);
        Start("Gateway", gatewayPort, gatewaySettings);
        baseAddress = new Uri($"http://127.0.0.1:{gatewayPort}");
        foreach (var port in ports) await WaitForPort(port);
    }

    /// <summary>A plain HttpClient: every request goes over a socket to the gateway.</summary>
    public ApiClient Client() => new(new HttpClient { BaseAddress = baseAddress });

    public async Task DisposeAsync()
    {
        foreach (var service in services)
        {
            service.Kill(entireProcessTree: true);
            await service.WaitForExitAsync();
            service.Dispose();
        }
        await postgres.DisposeAsync();
    }

    void Start(string project, int port, Dictionary<string, string> environment)
    {
        // <submission>/IntegrationTests/bin/<configuration>/net10.0/
        var output = new DirectoryInfo(AppContext.BaseDirectory);
        var configuration = output.Parent!.Name;
        var submission = output.Parent.Parent!.Parent!.Parent!.FullName;
        var folder = project == "Gateway"
            ? Path.Combine(submission, "src", "Conduit.Gateway", "bin", configuration, "net10.0")
            : Path.Combine(submission, "src", project, $"Conduit.{project}.Api", "bin", configuration, "net10.0");

        var start = new ProcessStartInfo("dotnet", $"conduit-{project.ToLowerInvariant()}.dll") { WorkingDirectory = folder, UseShellExecute = false };
        start.Environment["HOST"] = "127.0.0.1";
        start.Environment["PORT"] = port.ToString();
        foreach (var (name, value) in environment) start.Environment[name] = value;
        services.Add(Process.Start(start)!);
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
