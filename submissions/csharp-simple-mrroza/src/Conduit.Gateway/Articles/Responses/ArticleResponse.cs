using Conduit.Contracts;

namespace Conduit.Gateway.Articles.Responses;

public record ArticleResponse(Article Article)
{
    public static ArticleResponse From(ArticleMessage article) => new(Article.From(article));
}
