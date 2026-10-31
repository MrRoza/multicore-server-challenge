using Conduit.Comments.Abstractions;
using Conduit.Comments.Commands;
using Conduit.Comments.Queries;
using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;

namespace Conduit.Comments;

/// <summary>Uses the slice in the same process: one unit of work per call, and no network.</summary>
public sealed class CommentsClient(IDispatcher dispatcher, IUnitOfWork work) : ICommentsClient
{
    public async Task<ListCommentsResponseDto> List(long viewer, string slug)
    {
        await using (work.Begin())
        {
            return (await dispatcher.Query(new ListComments.Query(viewer, slug))).Unwrap();
        }
    }

    public async Task<CommentResponseDto> Add(long viewer, string slug, string? body)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new AddComment.Command(viewer, slug, body))).Unwrap();
            await work.Commit();
            return response;
        }
    }

    public async Task Delete(long viewer, string slug, string id)
    {
        await using (work.BeginTransaction())
        {
            (await dispatcher.Send(new DeleteComment.Command(viewer, slug, id))).Unwrap();
            await work.Commit();
        }
    }
}
