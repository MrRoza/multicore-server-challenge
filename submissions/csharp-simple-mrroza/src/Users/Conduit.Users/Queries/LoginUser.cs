using Conduit.Shared.Auth;
using Conduit.Shared.Cqrs;
using Conduit.Users.Abstractions;

namespace Conduit.Users.Queries;

public static class LoginUser
{
    public record Query(string? Email, string? Password) : IQuery<UserResponseDto>;

    public sealed class Handler(IUserRepository users, TokenService tokens) : IQueryHandler<Query, UserResponseDto>
    {
        public async Task<Result<UserResponseDto>> Handle(Query query)
        {
            if (string.IsNullOrEmpty(query.Email) || string.IsNullOrEmpty(query.Password))
                return Error.Validation("email and password are required");

            var row = await users.FindByEmail(query.Email);
            if (row is null || Password.IsTooLong(query.Password) || !BCrypt.Net.BCrypt.Verify(query.Password, row.PasswordHash))
                return Error.Unauthorized("wrong email or password");
            return new UserResponseDto(row.ToUser(tokens.Create(row.Id)));
        }
    }
}
