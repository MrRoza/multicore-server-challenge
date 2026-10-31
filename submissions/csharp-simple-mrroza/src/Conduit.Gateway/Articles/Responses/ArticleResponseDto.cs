using Conduit.Contracts;

namespace Conduit.Gateway.Articles.Responses;

public record ArticleResponseDto(ArticleDto Article)
{
    public static ArticleResponseDto From(ArticleMessage article) => new(ArticleDto.From(article));
}
