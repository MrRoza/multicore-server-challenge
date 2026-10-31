using System.Globalization;
using Conduit.Comments.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Comments.Commands;

public static class DeleteComment
{
    public record Command(long Viewer, string Slug, string Id) : ICommand<Unit>;

    public sealed class Handler(ICommentRepository comments) : ICommandHandler<Command, Unit>
    {
        public async Task<Result<Unit>> Handle(Command command)
        {
            if (!long.TryParse(command.Id, NumberStyles.None, CultureInfo.InvariantCulture, out var id)) return Error.NotFound("comment not found");

            var authorId = await comments.FindAuthorId(id, command.Slug);
            if (authorId is null) return Error.NotFound("comment not found");
            if (authorId != command.Viewer) return Error.Forbidden("only the author can do this");

            await comments.Delete(id);
            return new Unit();
        }
    }
}
