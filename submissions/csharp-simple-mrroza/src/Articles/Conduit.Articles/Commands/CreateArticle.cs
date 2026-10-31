using Conduit.Articles.Abstractions;
using Conduit.Articles.Queries;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles.Commands;

public static class CreateArticle
{
    public record Command(long Viewer, string? Title, string? Description, string? Body, string[] TagList) : ICommand<ArticleResponseDto>;

    public sealed class Handler(IArticleRepository articles, IDispatcher dispatcher) : ICommandHandler<Command, ArticleResponseDto>
    {
        public async Task<Result<ArticleResponseDto>> Handle(Command command)
        {
            if (string.IsNullOrEmpty(command.Title) || string.IsNullOrEmpty(command.Description) || string.IsNullOrEmpty(command.Body))
                return Error.Validation("title, description and body are required");
            if (command.TagList.Any(string.IsNullOrEmpty)) return Error.Validation("tags can't be empty");

            var id = await articles.Create(command.Viewer, command.Title, command.Description, command.Body, command.TagList);
            return await dispatcher.Query(new GetArticle.ById(command.Viewer, id));
        }
    }
}
