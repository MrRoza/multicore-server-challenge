using System.Security.Claims;
using Conduit.Profiles.Abstractions;
using Conduit.Shared;
using Conduit.Shared.Auth;

namespace Conduit.Http.Profiles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfilesApi(this IServiceCollection services) =>
        services.AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/profiles/{username}", async (string username, ClaimsPrincipal principal, IProfilesClient profiles) =>
            Results.Ok(await profiles.Get(principal.UserId(), username)))
            .AuthOptional();

        api.MapPost("/profiles/{username}/follow", async (string username, ClaimsPrincipal principal, IProfilesClient profiles) =>
            Results.Ok(await profiles.Follow(principal.UserId(), username)))
            .AuthRequired();

        api.MapDelete("/profiles/{username}/follow", async (string username, ClaimsPrincipal principal, IProfilesClient profiles) =>
            Results.Ok(await profiles.Unfollow(principal.UserId(), username)))
            .AuthRequired();
    }
}
