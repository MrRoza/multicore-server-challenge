namespace Conduit.Comments.Responses;

public record ListCommentsResponseDto(IEnumerable<CommentDto> Comments);
