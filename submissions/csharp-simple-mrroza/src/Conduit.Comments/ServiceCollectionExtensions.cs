using Conduit.Comments.Commands;
using Conduit.Comments.Queries;
using Conduit.Comments.Responses;
using Conduit.Shared;
using Conduit.Shared.Cqrs;

namespace Conduit.Comments;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComments(this IServiceCollection services) => services
        .AddQuery<ListComments.Query, ListCommentsResponse, ListComments.Handler>()
        .AddCommand<AddComment.Command, CommentResponse, AddComment.Handler>()
        .AddCommand<DeleteComment.Command, Unit, DeleteComment.Handler>()
        .AddEndpoints(endpoints => endpoints.MapGrpcService<CommentsGrpcService>());
}
