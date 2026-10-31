using System.Security.Claims;
using Conduit.Comments.Integrations;
using Conduit.Gateway.Comments.Requests;
using Conduit.Shared;
using Conduit.Shared.Auth;

namespace Conduit.Gateway.Comments;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCommentsApi(this IServiceCollection services, IConfiguration configuration) => services
        .AddCommentsClient(configuration)
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/articles/{slug}/comments", async (string slug, ClaimsPrincipal principal, ICommentsClient comments) =>
            Results.Ok(await comments.List(principal.UserId(), slug)))
            .AuthOptional();

        api.MapPost("/articles/{slug}/comments", async (string slug, AddCommentRequestDto request, ClaimsPrincipal principal, ICommentsClient comments) =>
            Results.Json(await comments.Add(principal.UserId(), slug, request.Comment?.Body), statusCode: StatusCodes.Status201Created))
            .AuthRequired();

        api.MapDelete("/articles/{slug}/comments/{id}", async (string slug, string id, ClaimsPrincipal principal, ICommentsClient comments) =>
        {
            await comments.Delete(principal.UserId(), slug, id);
            return Results.NoContent();
        }).AuthRequired();
    }
}
