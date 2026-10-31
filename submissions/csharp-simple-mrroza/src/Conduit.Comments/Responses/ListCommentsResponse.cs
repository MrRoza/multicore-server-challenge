namespace Conduit.Comments.Responses;

public record ListCommentsResponse(IEnumerable<Comment> Comments);
