using Conduit.Shared.Contracts;

namespace Conduit.Comments;

public record Comment(long Id, DateTime CreatedAt, DateTime UpdatedAt, string Body, Profile Author);
