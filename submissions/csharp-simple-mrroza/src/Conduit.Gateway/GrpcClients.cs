using Grpc.Net.ClientFactory;

namespace Conduit.Gateway;

public static class GrpcClients
{
    /// <summary>Adds a client for the slice whose address is in the given setting, such as USERS_URL.</summary>
    public static IServiceCollection AddSliceClient<TClient>(this IServiceCollection services, IConfiguration configuration, string setting)
        where TClient : class
    {
        var address = configuration[setting] ?? throw new InvalidOperationException($"{setting} is required");
        services.AddGrpcClient<TClient>((GrpcClientFactoryOptions options) => options.Address = new Uri(address))
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { EnableMultipleHttp2Connections = true });
        return services;
    }
}
