namespace Conduit.Articles.Responses;

public record ArticleListResponseDto(IEnumerable<ArticleDto> Articles, long ArticlesCount);
