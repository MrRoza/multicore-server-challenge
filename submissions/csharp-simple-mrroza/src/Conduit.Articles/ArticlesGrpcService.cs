using Conduit.Articles.Commands;
using Conduit.Articles.Queries;
using Conduit.Articles.Responses;
using Conduit.Contracts;
using Conduit.Shared.Cqrs;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;

namespace Conduit.Articles;

public sealed class ArticlesGrpcService(IDispatcher dispatcher) : ArticlesRpc.ArticlesRpcBase
{
    public override async Task<ArticleListMessage> List(ListArticlesMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Query(new ListArticles.Query(request.Viewer, request.Limit, request.Offset, request.Tag, request.Author, request.Favorited))).Unwrap());

    public override async Task<ArticleListMessage> Feed(GetFeedMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Query(new GetFeed.Query(request.Viewer, request.Limit, request.Offset))).Unwrap());

    public override async Task<ArticleMessage> Get(ArticleRequestMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Query(new GetArticle.Query(request.Viewer, request.Slug))).Unwrap().Article);

    public override async Task<ArticleMessage> Create(CreateArticleMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new CreateArticle.Command(request.Viewer, request.Title, request.Description, request.Body, request.TagList.ToArray()))).Unwrap().Article);

    public override async Task<ArticleMessage> Update(UpdateArticleMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new UpdateArticle.Command(request.Viewer, request.Slug, request.Title, request.Description, request.Body))).Unwrap().Article);

    public override async Task<Empty> Delete(ArticleRequestMessage request, ServerCallContext context)
    {
        (await dispatcher.Send(new DeleteArticle.Command(request.Viewer, request.Slug))).Unwrap();
        return new Empty();
    }

    public override async Task<ArticleMessage> Favorite(ArticleRequestMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new FavoriteArticle.Command(request.Viewer, request.Slug))).Unwrap().Article);

    public override async Task<ArticleMessage> Unfavorite(ArticleRequestMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new UnfavoriteArticle.Command(request.Viewer, request.Slug))).Unwrap().Article);

    static ArticleListMessage ToMessage(ArticleListResponseDto response) => new()
    {
        Articles = { response.Articles.Select(ToMessage) },
        ArticlesCount = response.ArticlesCount,
    };

    static ArticleMessage ToMessage(ArticleDto article) => new()
    {
        Slug = article.Slug,
        Title = article.Title,
        Description = article.Description,
        Body = article.Body,
        TagList = { article.TagList },
        CreatedAt = Timestamp.FromDateTime(article.CreatedAt),
        UpdatedAt = Timestamp.FromDateTime(article.UpdatedAt),
        Favorited = article.Favorited,
        FavoritesCount = article.FavoritesCount,
        Author = new ProfileMessage
        {
            Username = article.Author.Username,
            Bio = article.Author.Bio,
            Image = article.Author.Image,
            Following = article.Author.Following,
        },
    };
}
