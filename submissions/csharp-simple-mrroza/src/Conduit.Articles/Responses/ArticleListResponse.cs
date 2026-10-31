namespace Conduit.Articles.Responses;

public record ArticleListResponse(IEnumerable<Article> Articles, long ArticlesCount);
