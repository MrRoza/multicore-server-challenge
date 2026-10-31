using Conduit.Shared.Cqrs;
using Conduit.Users.Abstractions;

namespace Conduit.Users.Queries;

public static class GetCurrentUser
{
    public record Query(long UserId, string Token) : IQuery<UserResponseDto>;

    public sealed class Handler(IUserRepository users) : IQueryHandler<Query, UserResponseDto>
    {
        public async Task<Result<UserResponseDto>> Handle(Query query)
        {
            var row = await users.FindById(query.UserId);
            if (row is null) return Error.Unauthorized("this user no longer exists");
            return new UserResponseDto(row.ToUser(query.Token));
        }
    }
}
