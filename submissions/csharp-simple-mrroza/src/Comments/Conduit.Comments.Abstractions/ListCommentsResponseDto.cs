namespace Conduit.Comments.Abstractions;

public record ListCommentsResponseDto(IEnumerable<CommentDto> Comments);
