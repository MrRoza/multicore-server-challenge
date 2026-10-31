namespace Conduit.Gateway.Articles.Requests;

public record CreateArticleFields(string? Title, string? Description, string? Body, string?[]? TagList);
