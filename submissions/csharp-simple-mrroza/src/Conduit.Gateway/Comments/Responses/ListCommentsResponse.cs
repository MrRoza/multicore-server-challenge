namespace Conduit.Gateway.Comments.Responses;

public record ListCommentsResponse(IEnumerable<Comment> Comments);
