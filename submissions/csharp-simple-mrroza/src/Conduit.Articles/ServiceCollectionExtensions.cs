using Conduit.Articles.Commands;
using Conduit.Articles.Queries;
using Conduit.Articles.Responses;
using Conduit.Shared;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticles(this IServiceCollection services) => services
        .AddQuery<ListArticles.Query, ArticleListResponse, ListArticles.Handler>()
        .AddQuery<GetFeed.Query, ArticleListResponse, GetFeed.Handler>()
        .AddQuery<GetArticle.Query, ArticleResponse, GetArticle.Handler>()
        .AddQuery<GetArticle.ById, ArticleResponse, GetArticle.Handler>()
        .AddCommand<CreateArticle.Command, ArticleResponse, CreateArticle.Handler>()
        .AddCommand<UpdateArticle.Command, ArticleResponse, UpdateArticle.Handler>()
        .AddCommand<DeleteArticle.Command, Unit, DeleteArticle.Handler>()
        .AddCommand<FavoriteArticle.Command, ArticleResponse, FavoriteArticle.Handler>()
        .AddCommand<UnfavoriteArticle.Command, ArticleResponse, UnfavoriteArticle.Handler>()
        .AddEndpoints(endpoints => endpoints.MapGrpcService<ArticlesGrpcService>());
}
