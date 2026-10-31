using Conduit.Comments.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Comments.Commands;

public static class AddComment
{
    public record Command(long Viewer, string Slug, string? Body) : ICommand<CommentResponseDto>;

    public sealed class Handler(ICommentRepository comments) : ICommandHandler<Command, CommentResponseDto>
    {
        public async Task<Result<CommentResponseDto>> Handle(Command command)
        {
            if (string.IsNullOrEmpty(command.Body)) return Error.Validation("body is required");
            if (await comments.FindArticleId(command.Slug) is not { } articleId) return Error.NotFound("article not found");

            return new CommentResponseDto(await comments.Add(command.Viewer, articleId, command.Body));
        }
    }
}
