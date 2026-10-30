using System.Security.Claims;
using Conduit.Features.Profiles.Commands;
using Conduit.Features.Profiles.Queries;
using Conduit.Features.Profiles.Responses;
using Conduit.Shared;
using Conduit.Shared.Auth;
using Conduit.Shared.Cqrs;

namespace Conduit.Features.Profiles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfiles(this IServiceCollection services) => services
        .AddQuery<GetProfile.Query, ProfileResponse, GetProfile.Handler>()
        .AddCommand<FollowUser.Command, ProfileResponse, FollowUser.Handler>()
        .AddCommand<UnfollowUser.Command, ProfileResponse, UnfollowUser.Handler>()
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder api)
    {
        api.MapGet("/profiles/{username}", async (string username, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Query(new GetProfile.Query(principal.UserId(), username))).ToHttp())
            .AuthOptional();

        api.MapPost("/profiles/{username}/follow", async (string username, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new FollowUser.Command(principal.UserId(), username))).ToHttp())
            .AuthRequired();

        api.MapDelete("/profiles/{username}/follow", async (string username, ClaimsPrincipal principal, IDispatcher dispatcher) =>
            (await dispatcher.Send(new UnfollowUser.Command(principal.UserId(), username))).ToHttp())
            .AuthRequired();
    }
}
