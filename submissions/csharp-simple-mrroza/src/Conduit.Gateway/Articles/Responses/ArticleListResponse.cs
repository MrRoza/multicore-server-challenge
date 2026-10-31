using Conduit.Contracts;

namespace Conduit.Gateway.Articles.Responses;

public record ArticleListResponse(IEnumerable<Article> Articles, long ArticlesCount)
{
    public static ArticleListResponse From(ArticleListMessage list) => new(list.Articles.Select(Article.From), list.ArticlesCount);
}
