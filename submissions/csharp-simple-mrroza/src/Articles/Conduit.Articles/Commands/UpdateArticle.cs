using Conduit.Articles.Abstractions;
using Conduit.Articles.Queries;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles.Commands;

public static class UpdateArticle
{
    public record Command(long Viewer, string Slug, string? Title, string? Description, string? Body) : ICommand<ArticleResponseDto>;

    public sealed class Handler(IArticleRepository articles, IDispatcher dispatcher) : ICommandHandler<Command, ArticleResponseDto>
    {
        public async Task<Result<ArticleResponseDto>> Handle(Command command)
        {
            if (command.Title == "" || command.Description == "" || command.Body == "") return Error.Validation("title, description and body can't be empty");

            return await (await ArticleOwnership.FindOwnId(articles, command.Slug, command.Viewer)).Then(async id =>
            {
                await articles.Update(id, command.Title, command.Description, command.Body);
                return await dispatcher.Query(new GetArticle.ById(command.Viewer, id));
            });
        }
    }
}
