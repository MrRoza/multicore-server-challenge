using System.Security.Claims;
using Conduit.Contracts;
using Conduit.Gateway.Comments.Requests;
using Conduit.Gateway.Comments.Responses;
using Conduit.Shared;
using Conduit.Shared.Auth;

namespace Conduit.Gateway.Comments;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommentsApi(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<CommentsRpc.CommentsRpcClient>(configuration, "COMMENTS_URL")
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/articles/{slug}/comments", async (string slug, ClaimsPrincipal principal, CommentsRpc.CommentsRpcClient comments) =>
        {
            var list = await comments.ListAsync(new ListCommentsMessage { Viewer = principal.UserId(), Slug = slug });
            return Results.Ok(new ListCommentsResponse(list.Comments.Select(Comment.From)));
        }).AuthOptional();

        api.MapPost("/articles/{slug}/comments", async (string slug, AddCommentRequest request, ClaimsPrincipal principal, CommentsRpc.CommentsRpcClient comments) =>
        {
            var comment = await comments.AddAsync(new AddCommentMessage { Viewer = principal.UserId(), Slug = slug, Body = request.Comment?.Body });
            return Results.Json(new CommentResponse(Comment.From(comment)), statusCode: StatusCodes.Status201Created);
        }).AuthRequired();

        api.MapDelete("/articles/{slug}/comments/{id}", async (string slug, string id, ClaimsPrincipal principal, CommentsRpc.CommentsRpcClient comments) =>
        {
            await comments.DeleteAsync(new DeleteCommentMessage { Viewer = principal.UserId(), Slug = slug, Id = id });
            return Results.NoContent();
        }).AuthRequired();
    }
}
