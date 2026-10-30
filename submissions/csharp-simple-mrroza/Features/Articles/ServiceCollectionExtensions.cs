using System.Security.Claims;
using Conduit.Shared;

namespace Conduit.Features.Articles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticles(this IServiceCollection services) => services
        .AddQuery<ListArticles.Query, ArticleList, ListArticles.Handler>()
        .AddQuery<GetFeed.Query, ArticleList, GetFeed.Handler>()
        .AddQuery<GetArticle.Query, ArticleResponse, GetArticle.Handler>()
        .AddQuery<GetArticle.ById, ArticleResponse, GetArticle.Handler>()
        .AddCommand<CreateArticle.Command, ArticleResponse, CreateArticle.Handler>()
        .AddCommand<UpdateArticle.Command, ArticleResponse, UpdateArticle.Handler>()
        .AddCommand<DeleteArticle.Command, Unit, DeleteArticle.Handler>()
        .AddCommand<FavoriteArticle.Command, ArticleResponse, FavoriteArticle.Handler>()
        .AddCommand<UnfavoriteArticle.Command, ArticleResponse, UnfavoriteArticle.Handler>()
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/articles", async (string? limit, string? offset, string? tag, string? author, string? favorited, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Query(new ListArticles.Query(principal.UserId(), limit, offset, tag, author, favorited))).ToHttp())
            .AuthOptional();

        api.MapGet("/articles/feed", async (string? limit, string? offset, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Query(new GetFeed.Query(principal.UserId(), limit, offset))).ToHttp())
            .AuthRequired();

        api.MapGet("/articles/{slug}", async (string slug, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Query(new GetArticle.Query(principal.UserId(), slug))).ToHttp())
            .AuthOptional();

        api.MapPost("/articles", async (CreateArticle.Request request, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new CreateArticle.Command(principal.UserId(), request.Article))).ToHttp(StatusCodes.Status201Created))
            .AuthRequired();

        api.MapPut("/articles/{slug}", async (string slug, UpdateArticle.Request request, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new UpdateArticle.Command(principal.UserId(), slug, request.Article))).ToHttp())
            .AuthRequired();

        api.MapDelete("/articles/{slug}", async (string slug, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new DeleteArticle.Command(principal.UserId(), slug))).ToNoContent())
            .AuthRequired();

        api.MapPost("/articles/{slug}/favorite", async (string slug, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new FavoriteArticle.Command(principal.UserId(), slug))).ToHttp())
            .AuthRequired();

        api.MapDelete("/articles/{slug}/favorite", async (string slug, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new UnfavoriteArticle.Command(principal.UserId(), slug))).ToHttp())
            .AuthRequired();
    }
}
