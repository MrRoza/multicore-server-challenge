using System.Security.Claims;
using Conduit.Gateway.Users.Requests;
using Conduit.Shared;
using Conduit.Shared.Auth;
using Conduit.Users.Integrations;

namespace Conduit.Gateway.Users;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsersApi(this IServiceCollection services, IConfiguration configuration) => services
        .AddUsersClient(configuration)
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/users", async (RegisterUserRequestDto request, IUsersClient users) =>
            Results.Json(await users.Register(request.User?.Username, request.User?.Email, request.User?.Password), statusCode: StatusCodes.Status201Created));

        api.MapPost("/users/login", async (LoginUserRequestDto request, IUsersClient users) =>
            Results.Ok(await users.Login(request.User?.Email, request.User?.Password)));

        api.MapGet("/user", async (ClaimsPrincipal principal, HttpRequest http, IUsersClient users) =>
            Results.Ok(await users.GetCurrent(principal.UserId(), TokenAuth.PresentedToken(http)!)))
            .AuthRequired();

        api.MapPut("/user", async (UpdateUserRequestDto request, ClaimsPrincipal principal, HttpRequest http, IUsersClient users) =>
            request.User is not { } user
                ? ApiError.Result(StatusCodes.Status422UnprocessableEntity, "user is required")
                : Results.Ok(await users.Update(principal.UserId(), TokenAuth.PresentedToken(http)!, user.Email, user.Username, user.Password, user.Bio, user.Image)))
            .AuthRequired();
    }
}
