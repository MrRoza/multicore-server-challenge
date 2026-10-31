using Conduit.Articles.Abstractions;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles;

public static class ArticleOwnership
{
    /// <summary>The id of an article the viewer wrote: 404 if it doesn't exist, 403 if someone else wrote it.</summary>
    public static async Task<Result<long>> FindOwnId(IArticleRepository articles, string slug, long viewer)
    {
        var owner = await articles.FindOwner(slug);
        if (owner is null) return Error.NotFound("article not found");
        if (owner.AuthorId != viewer) return Error.Forbidden("only the author can do this");
        return owner.Id;
    }
}
