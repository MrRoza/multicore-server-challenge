using Conduit.Articles.Abstractions;
using Conduit.Articles.Integrations.Protos;
using Conduit.Shared.Contracts;

namespace Conduit.Articles.Integrations;

public sealed class ArticlesGrpcClient(ArticlesRpc.ArticlesRpcClient rpc) : IArticlesClient
{
    public async Task<ArticleListResponseDto> List(long viewer, string? limit, string? offset, string? tag, string? author, string? favorited) =>
        ToDto(await rpc.ListAsync(new ListArticlesMessage { Viewer = viewer, Limit = limit, Offset = offset, Tag = tag, Author = author, Favorited = favorited }));

    public async Task<ArticleListResponseDto> Feed(long viewer, string? limit, string? offset) =>
        ToDto(await rpc.FeedAsync(new GetFeedMessage { Viewer = viewer, Limit = limit, Offset = offset }));

    public async Task<ArticleResponseDto> Get(long viewer, string slug) =>
        new(ToDto(await rpc.GetAsync(new ArticleRequestMessage { Viewer = viewer, Slug = slug })));

    public async Task<ArticleResponseDto> Create(long viewer, string? title, string? description, string? body, IEnumerable<string> tags) =>
        new(ToDto(await rpc.CreateAsync(new CreateArticleMessage { Viewer = viewer, Title = title, Description = description, Body = body, TagList = { tags } })));

    public async Task<ArticleResponseDto> Update(long viewer, string slug, string? title, string? description, string? body) =>
        new(ToDto(await rpc.UpdateAsync(new UpdateArticleMessage { Viewer = viewer, Slug = slug, Title = title, Description = description, Body = body })));

    public async Task Delete(long viewer, string slug) =>
        await rpc.DeleteAsync(new ArticleRequestMessage { Viewer = viewer, Slug = slug });

    public async Task<ArticleResponseDto> Favorite(long viewer, string slug) =>
        new(ToDto(await rpc.FavoriteAsync(new ArticleRequestMessage { Viewer = viewer, Slug = slug })));

    public async Task<ArticleResponseDto> Unfavorite(long viewer, string slug) =>
        new(ToDto(await rpc.UnfavoriteAsync(new ArticleRequestMessage { Viewer = viewer, Slug = slug })));

    static ArticleListResponseDto ToDto(ArticleListMessage list) => new(list.Articles.Select(ToDto), list.ArticlesCount);

    static ArticleDto ToDto(ArticleMessage article) => new(
        article.Slug,
        article.Title,
        article.Description,
        article.Body,
        article.TagList.ToArray(),
        article.CreatedAt.ToDateTime(),
        article.UpdatedAt.ToDateTime(),
        article.Favorited,
        article.FavoritesCount,
        new ProfileDto(article.Author.Username, article.Author.Bio, article.Author.Image, article.Author.Following));
}
