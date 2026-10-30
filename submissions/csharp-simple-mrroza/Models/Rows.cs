namespace Conduit.Models;

// Database rows, filled in by Dapper.

public class UserRow
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Bio { get; init; }
    public string? Image { get; init; }
    public string PasswordHash { get; init; } = "";

    public User ToUser(string token) => new(Email, token, Username, Bio, Image);
}

public class AuthorRow
{
    public long Id { get; init; }
    public string Username { get; init; } = "";
    public string? Bio { get; init; }
    public string? Image { get; init; }
    public bool Following { get; init; }

    public Profile ToProfile() => new(Username, Bio, Image, Following);
}

public class ArticleRow : AuthorRow
{
    public string Slug { get; init; } = "";
    public string Title { get; init; } = "";
    public string Description { get; init; } = "";
    public string? Body { get; init; }
    public string[] TagList { get; init; } = [];
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }
    public bool Favorited { get; init; }
    public long FavoritesCount { get; init; }
    public long AuthorId { get; init; }

    public Article ToArticle() =>
        new(Slug, Title, Description, Body, TagList, CreatedAt, UpdatedAt, Favorited, FavoritesCount, ToProfile());
}

public class CommentRow : AuthorRow
{
    public string Body { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime UpdatedAt { get; init; }

    public Comment ToComment() => new(Id, CreatedAt, UpdatedAt, Body, ToProfile());
}
