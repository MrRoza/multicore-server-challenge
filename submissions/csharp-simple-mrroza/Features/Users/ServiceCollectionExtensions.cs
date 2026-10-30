using System.Security.Claims;
using Conduit.Features.Users.Commands;
using Conduit.Features.Users.Queries;
using Conduit.Features.Users.Requests;
using Conduit.Features.Users.Responses;
using Conduit.Shared;
using Conduit.Shared.Auth;
using Conduit.Shared.Cqrs;

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
        api.MapPost("/users", async (RegisterUserRequest request, IDispatcher dispatcher) =>
            (await dispatcher.Send(request.User ?? new RegisterUser.Command(null, null, null))).ToHttp(StatusCodes.Status201Created));

        api.MapPost("/users/login", async (LoginUserRequest request, IDispatcher dispatcher) =>
            (await dispatcher.Query(request.User ?? new LoginUser.Query(null, null))).ToHttp());

        api.MapGet("/user", async (ClaimsPrincipal principal, HttpRequest http, IDispatcher dispatcher) =>
            (await dispatcher.Query(new GetCurrentUser.Query(principal.UserId(), TokenAuth.PresentedToken(http)!))).ToHttp())
            .AuthRequired();

        api.MapPut("/user", async (UpdateUserRequest request, ClaimsPrincipal principal, HttpRequest http, IDispatcher dispatcher) =>
            (await dispatcher.Send(new UpdateUser.Command(principal.UserId(), TokenAuth.PresentedToken(http)!, request.User))).ToHttp())
            .AuthRequired();
    }
}
