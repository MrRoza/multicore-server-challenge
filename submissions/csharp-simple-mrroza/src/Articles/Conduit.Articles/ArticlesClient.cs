using Conduit.Articles.Abstractions;
using Conduit.Articles.Commands;
using Conduit.Articles.Queries;
using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;

namespace Conduit.Articles;

/// <summary>Uses the slice in the same process: one unit of work per call, and no network.</summary>
public sealed class ArticlesClient(IDispatcher dispatcher, IUnitOfWork work) : IArticlesClient
{
    public async Task<ArticleListResponseDto> List(long viewer, string? limit, string? offset, string? tag, string? author, string? favorited)
    {
        await using (work.Begin())
        {
            return (await dispatcher.Query(new ListArticles.Query(viewer, limit, offset, tag, author, favorited))).Unwrap();
        }
    }

    public async Task<ArticleListResponseDto> Feed(long viewer, string? limit, string? offset)
    {
        await using (work.Begin())
        {
            return (await dispatcher.Query(new GetFeed.Query(viewer, limit, offset))).Unwrap();
        }
    }

    public async Task<ArticleResponseDto> Get(long viewer, string slug)
    {
        await using (work.Begin())
        {
            return (await dispatcher.Query(new GetArticle.Query(viewer, slug))).Unwrap();
        }
    }

    public async Task<ArticleResponseDto> Create(long viewer, string? title, string? description, string? body, IEnumerable<string> tags)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new CreateArticle.Command(viewer, title, description, body, tags.ToArray()))).Unwrap();
            await work.Commit();
            return response;
        }
    }

    public async Task<ArticleResponseDto> Update(long viewer, string slug, string? title, string? description, string? body)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new UpdateArticle.Command(viewer, slug, title, description, body))).Unwrap();
            await work.Commit();
            return response;
        }
    }

    public async Task Delete(long viewer, string slug)
    {
        await using (work.BeginTransaction())
        {
            (await dispatcher.Send(new DeleteArticle.Command(viewer, slug))).Unwrap();
            await work.Commit();
        }
    }

    public async Task<ArticleResponseDto> Favorite(long viewer, string slug)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new FavoriteArticle.Command(viewer, slug))).Unwrap();
            await work.Commit();
            return response;
        }
    }

    public async Task<ArticleResponseDto> Unfavorite(long viewer, string slug)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new UnfavoriteArticle.Command(viewer, slug))).Unwrap();
            await work.Commit();
            return response;
        }
    }
}
