using System.Security.Claims;
using Conduit.Shared;

namespace Conduit.Features.Comments;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComments(this IServiceCollection services) => services
        .AddQuery<ListComments.Query, ListComments.Response, ListComments.Handler>()
        .AddCommand<AddComment.Command, CommentResponse, AddComment.Handler>()
        .AddCommand<DeleteComment.Command, Unit, DeleteComment.Handler>()
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/articles/{slug}/comments", async (string slug, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Query(new ListComments.Query(principal.UserId(), slug))).ToHttp())
            .AuthOptional();

        api.MapPost("/articles/{slug}/comments", async (string slug, AddComment.Request request, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new AddComment.Command(principal.UserId(), slug, request.Comment?.Body))).ToHttp(StatusCodes.Status201Created))
            .AuthRequired();

        api.MapDelete("/articles/{slug}/comments/{id}", async (string slug, string id, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new DeleteComment.Command(principal.UserId(), slug, id))).ToNoContent())
            .AuthRequired();
    }
}
