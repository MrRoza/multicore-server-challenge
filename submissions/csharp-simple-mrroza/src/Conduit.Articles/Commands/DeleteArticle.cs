using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Articles.Commands;

public static class DeleteArticle
{
    public record Command(long Viewer, string Slug) : ICommand<Unit>;

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, Unit>
    {
        public async Task<Result<Unit>> Handle(Command command)
        {
            await using var db = await data.OpenConnectionAsync();
            return await (await ArticleLookup.FindOwnId(db, command.Slug, command.Viewer)).Then<Unit>(async id =>
            {
                await db.ExecuteAsync("DELETE FROM articles WHERE id = @id", new { id });
                return new Unit();
            });
        }
    }
}
