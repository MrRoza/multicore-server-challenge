using Conduit.Contracts;
using Conduit.Profiles.Commands;
using Conduit.Profiles.Queries;
using Conduit.Profiles.Responses;
using Conduit.Shared.Cqrs;
using Grpc.Core;

namespace Conduit.Profiles;

public sealed class ProfilesGrpcService(IDispatcher dispatcher) : ProfilesRpc.ProfilesRpcBase
{
    public override async Task<ProfileMessage> Get(ProfileRequestMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Query(new GetProfile.Query(request.Viewer, request.Username))).Unwrap());

    public override async Task<ProfileMessage> Follow(ProfileRequestMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new FollowUser.Command(request.Viewer, request.Username))).Unwrap());

    public override async Task<ProfileMessage> Unfollow(ProfileRequestMessage request, ServerCallContext context) =>
        ToMessage((await dispatcher.Send(new UnfollowUser.Command(request.Viewer, request.Username))).Unwrap());

    static ProfileMessage ToMessage(ProfileResponse response) => new()
    {
        Username = response.Profile.Username,
        Bio = response.Profile.Bio,
        Image = response.Profile.Image,
        Following = response.Profile.Following,
    };
}
