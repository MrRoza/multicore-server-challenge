using Conduit.Contracts;
using Conduit.Gateway.Profiles.Responses;
using Conduit.Shared.Contracts;

namespace Conduit.Gateway.Comments.Responses;

public record Comment(long Id, DateTime CreatedAt, DateTime UpdatedAt, string Body, Profile Author)
{
    public static Comment From(CommentMessage comment) => new(
        comment.Id,
        comment.CreatedAt.ToDateTime(),
        comment.UpdatedAt.ToDateTime(),
        comment.Body,
        ProfileResponse.ToProfile(comment.Author));
}
