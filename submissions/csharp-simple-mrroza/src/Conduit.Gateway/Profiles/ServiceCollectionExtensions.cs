using System.Security.Claims;
using Conduit.Contracts;
using Conduit.Gateway.Profiles.Responses;
using Conduit.Shared;
using Conduit.Shared.Auth;

namespace Conduit.Gateway.Profiles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfilesApi(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<ProfilesRpc.ProfilesRpcClient>(configuration, "PROFILES_URL")
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/profiles/{username}", async (string username, ClaimsPrincipal principal, ProfilesRpc.ProfilesRpcClient profiles) =>
            Results.Ok(ProfileResponse.From(await profiles.GetAsync(new ProfileRequestMessage { Viewer = principal.UserId(), Username = username }))))
            .AuthOptional();

        api.MapPost("/profiles/{username}/follow", async (string username, ClaimsPrincipal principal, ProfilesRpc.ProfilesRpcClient profiles) =>
            Results.Ok(ProfileResponse.From(await profiles.FollowAsync(new ProfileRequestMessage { Viewer = principal.UserId(), Username = username }))))
            .AuthRequired();

        api.MapDelete("/profiles/{username}/follow", async (string username, ClaimsPrincipal principal, ProfilesRpc.ProfilesRpcClient profiles) =>
            Results.Ok(ProfileResponse.From(await profiles.UnfollowAsync(new ProfileRequestMessage { Viewer = principal.UserId(), Username = username }))))
            .AuthRequired();
    }
}
