using Conduit.Shared.Contracts;

namespace Conduit.Profiles.Abstractions;

public interface IProfileRepository
{
    Task<ProfileDto?> Find(long viewer, string username);
    Task<long?> FindUserId(string username);
    Task Follow(long viewer, long userId);
    Task Unfollow(long viewer, long userId);
}
