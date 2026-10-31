using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;
using Conduit.Users.Abstractions;
using Conduit.Users.Commands;
using Conduit.Users.Queries;

namespace Conduit.Users;

/// <summary>Uses the slice in the same process: one unit of work per call, and no network.</summary>
public sealed class UsersClient(IDispatcher dispatcher, IUnitOfWork work) : IUsersClient
{
    public async Task<UserResponseDto> Register(string? username, string? email, string? password)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new RegisterUser.Command(username, email, password))).Unwrap();
            await work.Commit();
            return response;
        }
    }

    public async Task<UserResponseDto> Login(string? email, string? password)
    {
        await using (work.Begin())
        {
            return (await dispatcher.Query(new LoginUser.Query(email, password))).Unwrap();
        }
    }

    public async Task<UserResponseDto> GetCurrent(long userId, string token)
    {
        await using (work.Begin())
        {
            return (await dispatcher.Query(new GetCurrentUser.Query(userId, token))).Unwrap();
        }
    }

    public async Task<UserResponseDto> Update(long userId, string token, string? email, string? username, string? password, string? bio, string? image)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new UpdateUser.Command(userId, token, email, username, password, bio, image))).Unwrap();
            await work.Commit();
            return response;
        }
    }
}
