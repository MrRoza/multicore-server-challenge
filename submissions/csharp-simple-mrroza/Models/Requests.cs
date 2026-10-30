namespace Conduit.Models;

public record RegisterRequest(RegisterRequest.Fields? User)
{
    public record Fields(string? Username, string? Email, string? Password);
}

public record LoginRequest(LoginRequest.Fields? User)
{
    public record Fields(string? Email, string? Password);
}

public record UpdateUserRequest(UpdateUserRequest.Fields? User)
{
    public record Fields(string? Email, string? Username, string? Password, string? Bio, string? Image);
}

public record CreateArticleRequest(CreateArticleRequest.Fields? Article)
{
    public record Fields(string? Title, string? Description, string? Body, string?[]? TagList);
}

public record UpdateArticleRequest(UpdateArticleRequest.Fields? Article)
{
    public record Fields(string? Title, string? Description, string? Body);
}

public record CreateCommentRequest(CreateCommentRequest.Fields? Comment)
{
    public record Fields(string? Body);
}
