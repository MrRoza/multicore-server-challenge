using System.Text.Json.Serialization;

namespace Conduit.Models;

public record User(string Email, string Token, string Username, string? Bio, string? Image);

public record Profile(string Username, string? Bio, string? Image, bool Following);

public record Article(
    string Slug,
    string Title,
    string Description,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Body, // lists leave it out
    string[] TagList,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    bool Favorited,
    long FavoritesCount,
    Profile Author);

public record Comment(long Id, DateTime CreatedAt, DateTime UpdatedAt, string Body, Profile Author);

public record UserResponse(User User);
public record ProfileResponse(Profile Profile);
public record ArticleResponse(Article Article);
public record ArticleListResponse(IEnumerable<Article> Articles, long ArticlesCount);
public record CommentResponse(Comment Comment);
public record CommentListResponse(IEnumerable<Comment> Comments);
public record TagListResponse(IEnumerable<string> Tags);
