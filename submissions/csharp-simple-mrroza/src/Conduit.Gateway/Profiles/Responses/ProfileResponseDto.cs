using Conduit.Contracts;
using Conduit.Shared.Contracts;

namespace Conduit.Gateway.Profiles.Responses;

public record ProfileResponseDto(ProfileDto Profile)
{
    public static ProfileDto ToProfile(ProfileMessage profile) => new(profile.Username, profile.Bio, profile.Image, profile.Following);

    public static ProfileResponseDto From(ProfileMessage profile) => new(ToProfile(profile));
}
