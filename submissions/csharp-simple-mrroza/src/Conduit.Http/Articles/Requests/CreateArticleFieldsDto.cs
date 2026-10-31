namespace Conduit.Http.Articles.Requests;

public record CreateArticleFieldsDto(string? Title, string? Description, string? Body, string?[]? TagList);
