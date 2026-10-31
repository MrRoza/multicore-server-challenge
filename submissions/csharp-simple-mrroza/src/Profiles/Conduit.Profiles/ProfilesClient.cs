using Conduit.Profiles.Abstractions;
using Conduit.Profiles.Commands;
using Conduit.Profiles.Queries;
using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;

namespace Conduit.Profiles;

/// <summary>Uses the slice in the same process: one unit of work per call, and no network.</summary>
public sealed class ProfilesClient(IDispatcher dispatcher, IUnitOfWork work) : IProfilesClient
{
    public async Task<ProfileResponseDto> Get(long viewer, string username)
    {
        await using (work.Begin())
        {
            return (await dispatcher.Query(new GetProfile.Query(viewer, username))).Unwrap();
        }
    }

    public async Task<ProfileResponseDto> Follow(long viewer, string username)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new FollowUser.Command(viewer, username))).Unwrap();
            await work.Commit();
            return response;
        }
    }

    public async Task<ProfileResponseDto> Unfollow(long viewer, string username)
    {
        await using (work.BeginTransaction())
        {
            var response = (await dispatcher.Send(new UnfollowUser.Command(viewer, username))).Unwrap();
            await work.Commit();
            return response;
        }
    }
}
