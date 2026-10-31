namespace Conduit.Gateway.Comments.Responses;

public record ListCommentsResponseDto(IEnumerable<CommentDto> Comments);
