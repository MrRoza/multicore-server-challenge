namespace Conduit.Comments.Abstractions;

/// <summary>How other parts of the app use the Comments slice. Failures arrive as exceptions.</summary>
public interface ICommentsClient
{
    Task<ListCommentsResponseDto> List(long viewer, string slug);
    Task<CommentResponseDto> Add(long viewer, string slug, string? body);
    Task Delete(long viewer, string slug, string id);
}
