using Conduit.Articles.Commands;
using Conduit.Articles.Queries;
using Conduit.Articles.Responses;
using Conduit.Shared;
using Conduit.Shared.Cqrs;

namespace Conduit.Articles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddArticles(this IServiceCollection services) => services
        .AddQuery<ListArticles.Query, ArticleListResponseDto, ListArticles.Handler>()
        .AddQuery<GetFeed.Query, ArticleListResponseDto, GetFeed.Handler>()
        .AddQuery<GetArticle.Query, ArticleResponseDto, GetArticle.Handler>()
        .AddQuery<GetArticle.ById, ArticleResponseDto, GetArticle.Handler>()
        .AddCommand<CreateArticle.Command, ArticleResponseDto, CreateArticle.Handler>()
        .AddCommand<UpdateArticle.Command, ArticleResponseDto, UpdateArticle.Handler>()
        .AddCommand<DeleteArticle.Command, Unit, DeleteArticle.Handler>()
        .AddCommand<FavoriteArticle.Command, ArticleResponseDto, FavoriteArticle.Handler>()
        .AddCommand<UnfavoriteArticle.Command, ArticleResponseDto, UnfavoriteArticle.Handler>()
        .AddEndpoints(endpoints => endpoints.MapGrpcService<ArticlesGrpcService>());
}
