namespace Conduit.Comments.Abstractions;

public interface ICommentRepository
{
    Task<long?> FindArticleId(string slug);
    Task<IEnumerable<CommentDto>?> ListBySlug(long viewer, string slug);
    Task<CommentDto> Add(long viewer, long articleId, string body);
    Task<long?> FindAuthorId(long id, string slug);
    Task Delete(long id);
}
