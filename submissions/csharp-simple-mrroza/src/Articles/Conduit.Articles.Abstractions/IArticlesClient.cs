namespace Conduit.Articles.Abstractions;

/// <summary>How other parts of the app use the Articles slice. Failures arrive as exceptions.</summary>
public interface IArticlesClient
{
    Task<ArticleListResponseDto> List(long viewer, string? limit, string? offset, string? tag, string? author, string? favorited);
    Task<ArticleListResponseDto> Feed(long viewer, string? limit, string? offset);
    Task<ArticleResponseDto> Get(long viewer, string slug);
    Task<ArticleResponseDto> Create(long viewer, string? title, string? description, string? body, IEnumerable<string> tags);
    Task<ArticleResponseDto> Update(long viewer, string slug, string? title, string? description, string? body);
    Task Delete(long viewer, string slug);
    Task<ArticleResponseDto> Favorite(long viewer, string slug);
    Task<ArticleResponseDto> Unfavorite(long viewer, string slug);
}
