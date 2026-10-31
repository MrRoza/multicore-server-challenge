using Conduit.Profiles.Commands;
using Conduit.Profiles.Queries;
using Conduit.Profiles.Responses;
using Conduit.Shared;

namespace Conduit.Profiles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfiles(this IServiceCollection services) => services
        .AddQuery<GetProfile.Query, ProfileResponse, GetProfile.Handler>()
        .AddCommand<FollowUser.Command, ProfileResponse, FollowUser.Handler>()
        .AddCommand<UnfollowUser.Command, ProfileResponse, UnfollowUser.Handler>()
        .AddEndpoints(endpoints => endpoints.MapGrpcService<ProfilesGrpcService>());
}
