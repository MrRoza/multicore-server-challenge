using Conduit.Comments.Abstractions;

namespace Conduit.Comments.Integrations;

/// <summary>Talks to the Comments service. Its failures arrive as RpcException.</summary>
public interface ICommentsClient
{
    Task<ListCommentsResponseDto> List(long viewer, string slug);
    Task<CommentResponseDto> Add(long viewer, string slug, string? body);
    Task Delete(long viewer, string slug, string id);
}
