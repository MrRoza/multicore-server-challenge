using Conduit.Profiles.Abstractions;
using Conduit.Profiles.Commands;
using Conduit.Profiles.Integrations.Protos;
using Conduit.Profiles.Queries;
using Conduit.Shared.Cqrs;
using Conduit.Shared.Data;
using Grpc.Core;

namespace Conduit.Profiles.Api;

public sealed class ProfilesGrpcService(IDispatcher dispatcher, IUnitOfWork work) : ProfilesRpc.ProfilesRpcBase
{
    public override async Task<ProfileMessage> Get(ProfileRequestMessage request, ServerCallContext context)
    {
        await using (work.Begin())
        {
            return ToMessage((await dispatcher.Query(new GetProfile.Query(request.Viewer, request.Username))).Unwrap());
        }
    }

    public override async Task<ProfileMessage> Follow(ProfileRequestMessage request, ServerCallContext context)
    {
        await using (work.BeginTransaction())
        {
            var message = ToMessage((await dispatcher.Send(new FollowUser.Command(request.Viewer, request.Username))).Unwrap());
            await work.Commit();
            return message;
        }
    }

    public override async Task<ProfileMessage> Unfollow(ProfileRequestMessage request, ServerCallContext context)
    {
        await using (work.BeginTransaction())
        {
            var message = ToMessage((await dispatcher.Send(new UnfollowUser.Command(request.Viewer, request.Username))).Unwrap());
            await work.Commit();
            return message;
        }
    }

    static ProfileMessage ToMessage(ProfileResponseDto response) => new()
    {
        Username = response.Profile.Username,
        Bio = response.Profile.Bio,
        Image = response.Profile.Image,
        Following = response.Profile.Following,
    };
}
