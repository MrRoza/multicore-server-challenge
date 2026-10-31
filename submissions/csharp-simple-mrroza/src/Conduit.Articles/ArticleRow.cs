using Conduit.Shared.Contracts;

namespace Conduit.Articles;

public class ArticleRow : ProfileRow
{
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Body { get; init; }
    public string[] TagList { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool Favorited { get; init; }
    public long FavoritesCount { get; init; }

    public Article ToArticle() =>
        new(Slug, Title, Description, Body, TagList, CreatedAt, UpdatedAt, Favorited, FavoritesCount, ToProfile());
}
