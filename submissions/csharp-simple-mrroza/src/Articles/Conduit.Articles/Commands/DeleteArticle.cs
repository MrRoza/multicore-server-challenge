using Conduit.Articles.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles.Commands;

public static class DeleteArticle
{
    public record Command(long Viewer, string Slug) : ICommand<Unit>;

    public sealed class Handler(IArticleRepository articles) : ICommandHandler<Command, Unit>
    {
        public async Task<Result<Unit>> Handle(Command command) =>
            await (await ArticleOwnership.FindOwnId(articles, command.Slug, command.Viewer)).Then<Unit>(async id =>
            {
                await articles.Delete(id);
                return new Unit();
            });
    }
}
