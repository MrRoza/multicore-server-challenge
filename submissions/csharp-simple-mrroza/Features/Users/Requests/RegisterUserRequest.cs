using Conduit.Features.Users.Commands;

namespace Conduit.Features.Users.Requests;

public record RegisterUserRequest(RegisterUser.Command? User);
