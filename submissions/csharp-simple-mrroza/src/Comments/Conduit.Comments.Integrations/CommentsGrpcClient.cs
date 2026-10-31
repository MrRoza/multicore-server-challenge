using Conduit.Comments.Abstractions;
using Conduit.Comments.Integrations.Protos;
using Conduit.Shared.Contracts;

namespace Conduit.Comments.Integrations;

public sealed class CommentsGrpcClient(CommentsRpc.CommentsRpcClient rpc) : ICommentsClient
{
    public async Task<ListCommentsResponseDto> List(long viewer, string slug) =>
        new((await rpc.ListAsync(new ListCommentsMessage { Viewer = viewer, Slug = slug })).Comments.Select(ToDto));

    public async Task<CommentResponseDto> Add(long viewer, string slug, string? body) =>
        new(ToDto(await rpc.AddAsync(new AddCommentMessage { Viewer = viewer, Slug = slug, Body = body })));

    public async Task Delete(long viewer, string slug, string id) =>
        await rpc.DeleteAsync(new DeleteCommentMessage { Viewer = viewer, Slug = slug, Id = id });

    static CommentDto ToDto(CommentMessage comment) => new(
        comment.Id,
        comment.CreatedAt.ToDateTime(),
        comment.UpdatedAt.ToDateTime(),
        comment.Body,
        new ProfileDto(comment.Author.Username, comment.Author.Bio, comment.Author.Image, comment.Author.Following));
}
