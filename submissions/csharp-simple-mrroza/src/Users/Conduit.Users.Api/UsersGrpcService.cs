using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;
using Conduit.Users.Abstractions;
using Conduit.Users.Commands;
using Conduit.Users.Integrations.Protos;
using Conduit.Users.Queries;
using Grpc.Core;

namespace Conduit.Users.Api;

public sealed class UsersGrpcService(IDispatcher dispatcher, IUnitOfWork work) : UsersRpc.UsersRpcBase
{
    public override async Task<UserMessage> Register(RegisterUserMessage request, ServerCallContext context)
    {
        await using (await work.BeginTransaction())
        {
            var message = ToMessage((await dispatcher.Send(new RegisterUser.Command(request.Username, request.Email, request.Password))).Unwrap());
            await work.Commit();
            return message;
        }
    }

    public override async Task<UserMessage> Login(LoginUserMessage request, ServerCallContext context)
    {
        await using (await work.Begin())
        {
            return ToMessage((await dispatcher.Query(new LoginUser.Query(request.Email, request.Password))).Unwrap());
        }
    }

    public override async Task<UserMessage> GetCurrent(GetCurrentUserMessage request, ServerCallContext context)
    {
        await using (await work.Begin())
        {
            return ToMessage((await dispatcher.Query(new GetCurrentUser.Query(request.UserId, request.Token))).Unwrap());
        }
    }

    public override async Task<UserMessage> Update(UpdateUserMessage request, ServerCallContext context)
    {
        await using (await work.BeginTransaction())
        {
            var message = ToMessage((await dispatcher.Send(new UpdateUser.Command(request.UserId, request.Token, request.Email, request.Username, request.Password, request.Bio, request.Image))).Unwrap());
            await work.Commit();
            return message;
        }
    }

    static UserMessage ToMessage(UserResponseDto response) => new()
    {
        Email = response.User.Email,
        Token = response.User.Token,
        Username = response.User.Username,
        Bio = response.User.Bio,
        Image = response.User.Image,
    };
}
