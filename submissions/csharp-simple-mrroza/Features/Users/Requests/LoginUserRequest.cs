using Conduit.Features.Users.Queries;

namespace Conduit.Features.Users.Requests;

public record LoginUserRequest(LoginUser.Query? User);
