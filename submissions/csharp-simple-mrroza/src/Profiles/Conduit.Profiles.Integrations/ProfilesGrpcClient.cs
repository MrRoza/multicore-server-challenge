using Conduit.Profiles.Abstractions;
using Conduit.Profiles.Integrations.Protos;
using Conduit.Shared.Contracts;

namespace Conduit.Profiles.Integrations;

public sealed class ProfilesGrpcClient(ProfilesRpc.ProfilesRpcClient rpc) : IProfilesClient
{
    public async Task<ProfileResponseDto> Get(long viewer, string username) =>
        ToDto(await rpc.GetAsync(new ProfileRequestMessage { Viewer = viewer, Username = username }));

    public async Task<ProfileResponseDto> Follow(long viewer, string username) =>
        ToDto(await rpc.FollowAsync(new ProfileRequestMessage { Viewer = viewer, Username = username }));

    public async Task<ProfileResponseDto> Unfollow(long viewer, string username) =>
        ToDto(await rpc.UnfollowAsync(new ProfileRequestMessage { Viewer = viewer, Username = username }));

    static ProfileResponseDto ToDto(ProfileMessage profile) => new(new ProfileDto(profile.Username, profile.Bio, profile.Image, profile.Following));
}
