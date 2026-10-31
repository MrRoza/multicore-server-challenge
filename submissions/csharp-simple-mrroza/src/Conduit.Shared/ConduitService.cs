using Conduit.Shared.Cqrs;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace Conduit.Shared;

/// <summary>The startup every slice's gRPC service shares. Only the slice it adds differs.</summary>
public static class ConduitService
{
    public static void Run(string[] args, Action<IServiceCollection> addSlice)
    {
        var builder = WebApplication.CreateBuilder(args);

        var host = builder.Configuration["HOST"] ?? "127.0.0.1";
        var port = builder.Configuration["PORT"] ?? "3000";
        builder.WebHost.UseUrls($"http://{host}:{port}");
        // gRPC needs HTTP/2, and without TLS Kestrel can't offer both versions on one port.
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ConfigureEndpointDefaults(endpoint => endpoint.Protocols = HttpProtocols.Http2));

        builder.Services.AddScoped<IDispatcher, Dispatcher>();
        builder.Services.AddGrpc();
        addSlice(builder.Services);

        var app = builder.Build();
        app.MapSlices();
        app.Run();
    }
}
