using Conduit.Contracts;
using Conduit.Shared.Cqrs;
using Conduit.Users.Commands;
using Conduit.Users.Queries;
using Conduit.Users.Responses;
using Grpc.Core;

namespace Conduit.Users;

public sealed class UsersGrpcService(IDispatcher dispatcher) : UsersRpc.UsersRpcBase
{
    public override async Task<UserMessage> Register(RegisterUserMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new RegisterUser.Command(request.Username, request.Email, request.Password))).Unwrap());

    public override async Task<UserMessage> Login(LoginUserMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Query(new LoginUser.Query(request.Email, request.Password))).Unwrap());

    public override async Task<UserMessage> GetCurrent(GetCurrentUserMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Query(new GetCurrentUser.Query(request.UserId, request.Token))).Unwrap());

    public override async Task<UserMessage> Update(UpdateUserMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new UpdateUser.Command(request.UserId, request.Token, request.Email, request.Username, request.Password, request.Bio, request.Image))).Unwrap());

    static UserMessage ToMessage(UserResponseDto response) => new()
    {
        Email = response.User.Email,
        Token = response.User.Token,
        Username = response.User.Username,
        Bio = response.User.Bio,
        Image = response.User.Image,
    };
}
