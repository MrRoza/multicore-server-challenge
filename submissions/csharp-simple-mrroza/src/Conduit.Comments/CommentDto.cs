using Conduit.Shared.Contracts;

namespace Conduit.Comments;

public record CommentDto(long Id, DateTime CreatedAt, DateTime UpdatedAt, string Body, ProfileDto Author);
