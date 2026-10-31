using System.Security.Claims;
using Conduit.Contracts;
using Conduit.Gateway.Articles.Requests;
using Conduit.Gateway.Articles.Responses;
using Conduit.Shared;
using Conduit.Shared.Auth;

namespace Conduit.Gateway.Articles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticlesApi(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<ArticlesRpc.ArticlesRpcClient>(configuration, "ARTICLES_URL")
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/articles", async (string? limit, string? offset, string? tag, string? author, string? favorited, ClaimsPrincipal principal, ArticlesRpc.ArticlesRpcClient articles) =>
            Results.Ok(ArticleListResponseDto.From(await articles.ListAsync(new ListArticlesMessage
            {
                Viewer = principal.UserId(),
                Limit = limit,
                Offset = offset,
                Tag = tag,
                Author = author,
                Favorited = favorited,
            })))).AuthOptional();

        api.MapGet("/articles/feed", async (string? limit, string? offset, ClaimsPrincipal principal, ArticlesRpc.ArticlesRpcClient articles) =>
            Results.Ok(ArticleListResponseDto.From(await articles.FeedAsync(new GetFeedMessage { Viewer = principal.UserId(), Limit = limit, Offset = offset }))))
            .AuthRequired();

        api.MapGet("/articles/{slug}", async (string slug, ClaimsPrincipal principal, ArticlesRpc.ArticlesRpcClient articles) =>
            Results.Ok(ArticleResponseDto.From(await articles.GetAsync(new ArticleRequestMessage { Viewer = principal.UserId(), Slug = slug }))))
            .AuthOptional();

        api.MapPost("/articles", async (CreateArticleRequestDto request, ClaimsPrincipal principal, ArticlesRpc.ArticlesRpcClient articles) =>
            Results.Json(ArticleResponseDto.From(await articles.CreateAsync(new CreateArticleMessage
            {
                Viewer = principal.UserId(),
                Title = request.Article?.Title,
                Description = request.Article?.Description,
                Body = request.Article?.Body,
                // A list in gRPC can't hold a null, and an empty tag is refused the same way.
                TagList = { (request.Article?.TagList ?? []).Select(tag => tag ?? "") },
            })), statusCode: StatusCodes.Status201Created)).AuthRequired();

        api.MapPut("/articles/{slug}", async (string slug, UpdateArticleRequestDto request, ClaimsPrincipal principal, ArticlesRpc.ArticlesRpcClient articles) =>
            request.Article is not { } article
                ? ApiError.Result(StatusCodes.Status422UnprocessableEntity, "article is required")
                : Results.Ok(ArticleResponseDto.From(await articles.UpdateAsync(new UpdateArticleMessage
                {
                    Viewer = principal.UserId(),
                    Slug = slug,
                    Title = article.Title,
                    Description = article.Description,
                    Body = article.Body,
                })))).AuthRequired();

        api.MapDelete("/articles/{slug}", async (string slug, ClaimsPrincipal principal, ArticlesRpc.ArticlesRpcClient articles) =>
        {
            await articles.DeleteAsync(new ArticleRequestMessage { Viewer = principal.UserId(), Slug = slug });
            return Results.NoContent();
        }).AuthRequired();

        api.MapPost("/articles/{slug}/favorite", async (string slug, ClaimsPrincipal principal, ArticlesRpc.ArticlesRpcClient articles) =>
            Results.Ok(ArticleResponseDto.From(await articles.FavoriteAsync(new ArticleRequestMessage { Viewer = principal.UserId(), Slug = slug }))))
            .AuthRequired();

        api.MapDelete("/articles/{slug}/favorite", async (string slug, ClaimsPrincipal principal, ArticlesRpc.ArticlesRpcClient articles) =>
            Results.Ok(ArticleResponseDto.From(await articles.UnfavoriteAsync(new ArticleRequestMessage { Viewer = principal.UserId(), Slug = slug }))))
            .AuthRequired();
    }
}
