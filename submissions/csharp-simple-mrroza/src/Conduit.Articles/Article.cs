using System.Text.Json.Serialization;
using Conduit.Shared.Contracts;

namespace Conduit.Articles;

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
