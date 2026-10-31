using Conduit.Shared.Contracts;

namespace Conduit.Comments;

public class CommentRowDto : ProfileRowDto
{
    public string Body { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public CommentDto ToComment() => new(Id, CreatedAt, UpdatedAt, Body, ToProfile());
}
