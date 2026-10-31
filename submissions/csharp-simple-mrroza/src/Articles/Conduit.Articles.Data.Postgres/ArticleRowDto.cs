using Conduit.Articles.Abstractions;
using Conduit.Shared.Contracts;

namespace Conduit.Articles.Data.Postgres;

public class ArticleRowDto : ProfileRowDto
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

    public ArticleDto ToArticle() =>
        new(Slug, Title, Description, Body, TagList, CreatedAt, UpdatedAt, Favorited, FavoritesCount, ToProfile());
}
