using Conduit.Contracts;

namespace Conduit.Gateway.Articles.Responses;

public record ArticleListResponseDto(IEnumerable<ArticleDto> Articles, long ArticlesCount)
{
    public static ArticleListResponseDto From(ArticleListMessage list) => new(list.Articles.Select(ArticleDto.From), list.ArticlesCount);
}
