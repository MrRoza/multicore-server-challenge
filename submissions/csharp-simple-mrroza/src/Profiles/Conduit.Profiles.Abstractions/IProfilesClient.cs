namespace Conduit.Profiles.Abstractions;

/// <summary>How other parts of the app use the Profiles slice. Failures arrive as exceptions.</summary>
public interface IProfilesClient
{
    Task<ProfileResponseDto> Get(long viewer, string username);
    Task<ProfileResponseDto> Follow(long viewer, string username);
    Task<ProfileResponseDto> Unfollow(long viewer, string username);
}
