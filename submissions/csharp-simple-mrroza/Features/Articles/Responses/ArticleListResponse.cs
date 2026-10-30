namespace Conduit.Features.Articles.Responses;

public record ArticleListResponse(IEnumerable<Article> Articles, long ArticlesCount);
