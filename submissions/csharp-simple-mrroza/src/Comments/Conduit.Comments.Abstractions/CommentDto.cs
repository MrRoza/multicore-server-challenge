using Conduit.Shared.Contracts;

namespace Conduit.Comments.Abstractions;

public record CommentDto(long Id, DateTime CreatedAt, DateTime UpdatedAt, string Body, ProfileDto Author);
