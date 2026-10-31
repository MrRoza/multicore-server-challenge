namespace Conduit.Comments.Abstractions;

public interface ICommentRepository
{
    Task<long?> FindArticleId(string slug);
    Task<IEnumerable<CommentDto>> List(long viewer, long articleId);
    Task<CommentDto> Add(long viewer, long articleId, string body);
    Task<long?> FindAuthorId(long id, string slug);
    Task Delete(long id);
}
