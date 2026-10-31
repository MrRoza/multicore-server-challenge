using System.Security.Claims;
using Conduit.Contracts;
using Conduit.Gateway.Users.Requests;
using Conduit.Gateway.Users.Responses;
using Conduit.Shared;
using Conduit.Shared.Auth;

namespace Conduit.Gateway.Users;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddUsersApi(this IServiceCollection services, IConfiguration configuration) => services
        .AddSliceClient<UsersRpc.UsersRpcClient>(configuration, "USERS_URL")
        .AddEndpoints(MapEndpoints);

    static void MapEndpoints(IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapPost("/users", async (RegisterUserRequest request, UsersRpc.UsersRpcClient users) =>
            Results.Json(UserResponse.From(await users.RegisterAsync(new RegisterUserMessage
            {
                Username = request.User?.Username,
                Email = request.User?.Email,
                Password = request.User?.Password,
            })), statusCode: StatusCodes.Status201Created));

        api.MapPost("/users/login", async (LoginUserRequest request, UsersRpc.UsersRpcClient users) =>
            Results.Ok(UserResponse.From(await users.LoginAsync(new LoginUserMessage
            {
                Email = request.User?.Email,
                Password = request.User?.Password,
            }))));

        api.MapGet("/user", async (ClaimsPrincipal principal, HttpRequest http, UsersRpc.UsersRpcClient users) =>
            Results.Ok(UserResponse.From(await users.GetCurrentAsync(new GetCurrentUserMessage
            {
                UserId = principal.UserId(),
                Token = TokenAuth.PresentedToken(http)!,
            })))).AuthRequired();

        api.MapPut("/user", async (UpdateUserRequest request, ClaimsPrincipal principal, HttpRequest http, UsersRpc.UsersRpcClient users) =>
            request.User is not { } user
                ? ApiError.Result(StatusCodes.Status422UnprocessableEntity, "user is required")
                : Results.Ok(UserResponse.From(await users.UpdateAsync(new UpdateUserMessage
                {
                    UserId = principal.UserId(),
                    Token = TokenAuth.PresentedToken(http)!,
                    Email = user.Email,
                    Username = user.Username,
                    Password = user.Password,
                    Bio = user.Bio,
                    Image = user.Image,
                })))).AuthRequired();
    }
}
