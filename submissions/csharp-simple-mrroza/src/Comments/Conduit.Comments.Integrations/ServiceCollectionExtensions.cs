using Conduit.Comments.Integrations.Protos;
using Conduit.Shared;

namespace Conduit.Comments.Integrations;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommentsClient(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<CommentsRpc.CommentsRpcClient>(configuration, "COMMENTS_URL")
        .AddTransient<ICommentsClient, CommentsGrpcClient>();
}
