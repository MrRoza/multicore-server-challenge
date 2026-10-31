namespace Conduit.Articles.Abstractions;

public record ArticleListResponseDto(IEnumerable<ArticleDto> Articles, long ArticlesCount);
