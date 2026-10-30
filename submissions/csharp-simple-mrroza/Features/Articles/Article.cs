using System.Text.Json.Serialization;
using Conduit.Features.Profiles;

namespace Conduit.Features.Articles;

public record Article(
    string Slug,
    string Title,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body, // lists leave it out
    string[] TagList,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Favorited,
    long FavoritesCount,
    Profile Author);
