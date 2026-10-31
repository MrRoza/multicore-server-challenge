using System.Text.Json.Serialization;
using Conduit.Contracts;
using Conduit.Gateway.Profiles.Responses;
using Conduit.Shared.Contracts;

namespace Conduit.Gateway.Articles.Responses;

public record ArticleDto(
    string Slug,
    string Title,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body, // lists leave it out
    string[] TagList,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Favorited,
    long FavoritesCount,
    ProfileDto Author)
{
    public static ArticleDto From(ArticleMessage article) => new(
        article.Slug,
        article.Title,
        article.Description,
        article.Body,
        article.TagList.ToArray(),
        article.CreatedAt.ToDateTime(),
        article.UpdatedAt.ToDateTime(),
        article.Favorited,
        article.FavoritesCount,
        ProfileResponseDto.ToProfile(article.Author));
}
