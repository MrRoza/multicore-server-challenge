using Conduit.Profiles.Abstractions;

namespace Conduit.Profiles.Integrations;

/// <summary>Talks to the Profiles service. Its failures arrive as RpcException.</summary>
public interface IProfilesClient
{
    Task<ProfileResponseDto> Get(long viewer, string username);
    Task<ProfileResponseDto> Follow(long viewer, string username);
    Task<ProfileResponseDto> Unfollow(long viewer, string username);
}
