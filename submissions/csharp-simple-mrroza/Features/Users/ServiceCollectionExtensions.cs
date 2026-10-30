using System.Security.Claims;
using Conduit.Shared;

namespace Conduit.Features.Users;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsers(this IServiceCollection services) => services
        .AddCommand<RegisterUser.Command, UserResponse, RegisterUser.Handler>()
        .AddQuery<LoginUser.Query, UserResponse, LoginUser.Handler>()
        .AddQuery<GetCurrentUser.Query, UserResponse, GetCurrentUser.Handler>()
        .AddCommand<UpdateUser.Command, UserResponse, UpdateUser.Handler>()
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder api)
    {
        api.MapPost("/users", async (RegisterUser.Request request, IDispatcher dispatcher) =>
            (await dispatcher.Send(request.User ?? new RegisterUser.Command(null, null, null))).ToHttp(StatusCodes.Status201Created));

        api.MapPost("/users/login", async (LoginUser.Request request, IDispatcher dispatcher) =>
            (await dispatcher.Query(request.User ?? new LoginUser.Query(null, null))).ToHttp());

        api.MapGet("/user", async (ClaimsPrincipal principal, HttpRequest http, IDispatcher dispatcher) =>
            (await dispatcher.Query(new GetCurrentUser.Query(principal.UserId(), TokenAuth.PresentedToken(http)!))).ToHttp())
            .AuthRequired();

        api.MapPut("/user", async (UpdateUser.Request request, ClaimsPrincipal principal, HttpRequest http, IDispatcher dispatcher) =>
            (await dispatcher.Send(new UpdateUser.Command(principal.UserId(), TokenAuth.PresentedToken(http)!, request.User))).ToHttp())
            .AuthRequired();
    }
}
