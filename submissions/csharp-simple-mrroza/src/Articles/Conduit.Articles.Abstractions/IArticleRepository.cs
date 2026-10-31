namespace Conduit.Articles.Abstractions;

public interface IArticleRepository
{
    Task<ArticleListResponseDto> List(long viewer, Page page, string? tag, string? author, string? favorited);
    Task<ArticleListResponseDto> Feed(long viewer, Page page);
    Task<ArticleDto?> FindBySlug(long viewer, string slug);
    Task<ArticleDto?> FindById(long viewer, long id);
    Task<long?> FindId(string slug);
    Task<ArticleOwnerDto?> FindOwner(string slug);
    Task<long> Create(long viewer, string title, string description, string body, string[] tags);
    Task Update(long id, string? title, string? description, string? body);
    Task Delete(long id);
    Task Favorite(long viewer, long id);
    Task Unfavorite(long viewer, long id);
}
