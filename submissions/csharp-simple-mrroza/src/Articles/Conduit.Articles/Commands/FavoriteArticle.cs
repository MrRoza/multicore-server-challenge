using Conduit.Articles.Abstractions;
using Conduit.Articles.Queries;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles.Commands;

public static class FavoriteArticle
{
    public record Command(long Viewer, string Slug) : ICommand<ArticleResponseDto>;

    public sealed class Handler(IArticleRepository articles, IDispatcher dispatcher) : ICommandHandler<Command, ArticleResponseDto>
    {
        public async Task<Result<ArticleResponseDto>> Handle(Command command)
        {
            if (await articles.FindId(command.Slug) is not { } id) return Error.NotFound("article not found");

            await articles.Favorite(command.Viewer, id);
            return await dispatcher.Query(new GetArticle.ById(command.Viewer, id));
        }
    }
}
