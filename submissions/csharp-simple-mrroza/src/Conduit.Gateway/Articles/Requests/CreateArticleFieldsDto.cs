namespace Conduit.Gateway.Articles.Requests;

public record CreateArticleFieldsDto(string? Title, string? Description, string? Body, string?[]? TagList);
