using Conduit.Comments.Abstractions;
using Conduit.Comments.Commands;
using Conduit.Comments.Integrations.Protos;
using Conduit.Comments.Queries;
using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Conduit.Comments.Api;

public sealed class CommentsGrpcService(IDispatcher dispatcher, IUnitOfWork work) : CommentsRpc.CommentsRpcBase
{
    public override async Task<CommentListMessage> List(ListCommentsMessage request, ServerCallContext context)
    {
        await using (await work.Begin())
        {
            return new() { Comments = { (await dispatcher.Query(new ListComments.Query(request.Viewer, request.Slug))).Unwrap().Comments.Select(ToMessage) } };
        }
    }

    public override async Task<CommentMessage> Add(AddCommentMessage request, ServerCallContext context)
    {
        await using (await work.BeginTransaction())
        {
            var message = ToMessage((await dispatcher.Send(new AddComment.Command(request.Viewer, request.Slug, request.Body))).Unwrap().Comment);
            await work.Commit();
            return message;
        }
    }

    public override async Task<Empty> Delete(DeleteCommentMessage request, ServerCallContext context)
    {
        await using (await work.BeginTransaction())
        {
            (await dispatcher.Send(new DeleteComment.Command(request.Viewer, request.Slug, request.Id))).Unwrap();
            await work.Commit();
            return new Empty();
        }
    }

    static CommentMessage ToMessage(CommentDto comment) => new()
    {
        Id = comment.Id,
        CreatedAt = Timestamp.FromDateTime(comment.CreatedAt),
        UpdatedAt = Timestamp.FromDateTime(comment.UpdatedAt),
        Body = comment.Body,
        Author = new CommentAuthorMessage
        {
            Username = comment.Author.Username,
            Bio = comment.Author.Bio,
            Image = comment.Author.Image,
            Following = comment.Author.Following,
        },
    };
}
