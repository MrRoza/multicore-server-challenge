using Conduit.Comments.Commands;
using Conduit.Comments.Queries;
using Conduit.Contracts;
using Conduit.Shared.Cqrs;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Conduit.Comments;

public sealed class CommentsGrpcService(IDispatcher dispatcher) : CommentsRpc.CommentsRpcBase
{
    public override async Task<CommentListMessage> List(ListCommentsMessage request, ServerCallContext context) =>
        new() { Comments = { (await dispatcher.Query(new ListComments.Query(request.Viewer, request.Slug))).Unwrap().Comments.Select(ToMessage) } };

    public override async Task<CommentMessage> Add(AddCommentMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new AddComment.Command(request.Viewer, request.Slug, request.Body))).Unwrap().Comment);

    public override async Task<Empty> Delete(DeleteCommentMessage request, ServerCallContext context)
    {
        (await dispatcher.Send(new DeleteComment.Command(request.Viewer, request.Slug, request.Id))).Unwrap();
        return new Empty();
    }

    static CommentMessage ToMessage(Comment comment) => new()
    {
        Id = comment.Id,
        CreatedAt = Timestamp.FromDateTime(comment.CreatedAt),
        UpdatedAt = Timestamp.FromDateTime(comment.UpdatedAt),
        Body = comment.Body,
        Author = new ProfileMessage
        {
            Username = comment.Author.Username,
            Bio = comment.Author.Bio,
            Image = comment.Author.Image,
            Following = comment.Author.Following,
        },
    };
}
