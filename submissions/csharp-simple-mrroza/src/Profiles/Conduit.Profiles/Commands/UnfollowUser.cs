using Conduit.Profiles.Abstractions;
using Conduit.Profiles.Queries;
using Conduit.Shared.Cqrs;

namespace Conduit.Profiles.Commands;

public static class UnfollowUser
{
    public record Command(long Viewer, string Username) : ICommand<ProfileResponseDto>;

    public sealed class Handler(IProfileRepository profiles, IDispatcher dispatcher) : ICommandHandler<Command, ProfileResponseDto>
    {
        public async Task<Result<ProfileResponseDto>> Handle(Command command)
        {
            if (await profiles.FindUserId(command.Username) is not { } userId) return Error.NotFound("user not found");

            await profiles.Unfollow(command.Viewer, userId);
            return await dispatcher.Query(new GetProfile.Query(command.Viewer, command.Username));
        }
    }
}
