using Conduit.Features.Profiles;

namespace Conduit.Features.Comments;

public record Comment(long Id, DateTime CreatedAt, DateTime UpdatedAt, string Body, Profile Author);
