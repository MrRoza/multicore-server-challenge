using System.Security.Claims;
using Conduit.Articles.Abstractions;
using Conduit.Http.Articles.Requests;
using Conduit.Shared;
using Conduit.Shared.Auth;

namespace Conduit.Http.Articles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticlesApi(this IServiceCollection services) =>
        services.AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/articles", async (string? limit, string? offset, string? tag, string? author, string? favorited, ClaimsPrincipal principal, IArticlesClient articles) =>
            Results.Ok(await articles.List(principal.UserId(), limit, offset, tag, author, favorited)))
            .AuthOptional();

        api.MapGet("/articles/feed", async (string? limit, string? offset, ClaimsPrincipal principal, IArticlesClient articles) =>
            Results.Ok(await articles.Feed(principal.UserId(), limit, offset)))
            .AuthRequired();

        api.MapGet("/articles/{slug}", async (string slug, ClaimsPrincipal principal, IArticlesClient articles) =>
            Results.Ok(await articles.Get(principal.UserId(), slug)))
            .AuthOptional();

        // A null tag is refused the same way as an empty one.
        api.MapPost("/articles", async (CreateArticleRequestDto request, ClaimsPrincipal principal, IArticlesClient articles) =>
            Results.Json(
                await articles.Create(principal.UserId(), request.Article?.Title, request.Article?.Description, request.Article?.Body, (request.Article?.TagList ?? []).Select(tag => tag ?? "")),
                statusCode: StatusCodes.Status201Created))
            .AuthRequired();

        api.MapPut("/articles/{slug}", async (string slug, UpdateArticleRequestDto request, ClaimsPrincipal principal, IArticlesClient articles) =>
            request.Article is not { } article
                ? ApiError.Result(StatusCodes.Status422UnprocessableEntity, "article is required")
                : Results.Ok(await articles.Update(principal.UserId(), slug, article.Title, article.Description, article.Body)))
            .AuthRequired();

        api.MapDelete("/articles/{slug}", async (string slug, ClaimsPrincipal principal, IArticlesClient articles) =>
        {
            await articles.Delete(principal.UserId(), slug);
            return Results.NoContent();
        }).AuthRequired();

        api.MapPost("/articles/{slug}/favorite", async (string slug, ClaimsPrincipal principal, IArticlesClient articles) =>
            Results.Ok(await articles.Favorite(principal.UserId(), slug)))
            .AuthRequired();

        api.MapDelete("/articles/{slug}/favorite", async (string slug, ClaimsPrincipal principal, IArticlesClient articles) =>
            Results.Ok(await articles.Unfavorite(principal.UserId(), slug)))
            .AuthRequired();
    }
}
