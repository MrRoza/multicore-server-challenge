using Conduit.Profiles.Abstractions;
using Conduit.Profiles.Commands;
using Conduit.Profiles.Queries;
using Conduit.Shared;

namespace Conduit.Profiles;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddProfiles(this IServiceCollection services) => services
        .AddQuery<GetProfile.Query, ProfileResponseDto, GetProfile.Handler>()
        .AddCommand<FollowUser.Command, ProfileResponseDto, FollowUser.Handler>()
        .AddCommand<UnfollowUser.Command, ProfileResponseDto, UnfollowUser.Handler>();
}
