using Conduit.Contracts;
using Conduit.Shared.Contracts;

namespace Conduit.Gateway.Profiles.Responses;

public record ProfileResponse(Profile Profile)
{
    public static Profile ToProfile(ProfileMessage profile) => new(profile.Username, profile.Bio, profile.Image, profile.Following);

    public static ProfileResponse From(ProfileMessage profile) => new(ToProfile(profile));
}
