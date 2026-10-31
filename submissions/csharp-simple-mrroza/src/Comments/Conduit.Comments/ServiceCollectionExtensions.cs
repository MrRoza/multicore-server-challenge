using Conduit.Comments.Abstractions;
using Conduit.Comments.Commands;
using Conduit.Comments.Queries;
using Conduit.Shared;
using Conduit.Shared.Cqrs;

namespace Conduit.Comments;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComments(this IServiceCollection services) => services
        .AddQuery<ListComments.Query, ListCommentsResponseDto, ListComments.Handler>()
        .AddCommand<AddComment.Command, CommentResponseDto, AddComment.Handler>()
        .AddCommand<DeleteComment.Command, Unit, DeleteComment.Handler>();
}
