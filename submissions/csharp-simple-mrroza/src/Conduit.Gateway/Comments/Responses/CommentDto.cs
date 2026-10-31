using Conduit.Contracts;
using Conduit.Gateway.Profiles.Responses;
using Conduit.Shared.Contracts;

namespace Conduit.Gateway.Comments.Responses;

public record CommentDto(long Id, DateTime CreatedAt, DateTime UpdatedAt, string Body, ProfileDto Author)
{
    public static CommentDto From(CommentMessage comment) => new(
        comment.Id,
        comment.CreatedAt.ToDateTime(),
        comment.UpdatedAt.ToDateTime(),
        comment.Body,
        ProfileResponseDto.ToProfile(comment.Author));
}
