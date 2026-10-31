using Conduit.Shared.Contracts;

namespace Conduit.Comments;

public class CommentRow : ProfileRow
{
    public string Body { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public Comment ToComment() => new(Id, CreatedAt, UpdatedAt, Body, ToProfile());
}
