using Conduit.Profiles.Abstractions;
using Conduit.Shared.Contracts;
using Conduit.Shared.Data;
using Dapper;

namespace Conduit.Profiles.Data.Postgres;

public sealed class ProfileRepository(IUnitOfWork work) : IProfileRepository
{
    public async Task<ProfileDto?> Find(long viewer, string username)
    {
        var row = await work.Connection.QuerySingleOrDefaultAsync<ProfileRowDto>("""
            SELECT u.id, u.username, u.bio, u.image,
                   EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @viewer AND fo.followee_id = u.id) AS following
              FROM users u WHERE u.username = @username
            """, new { viewer, username });
        return row?.ToProfile();
    }

    public async Task<long?> FindUserId(string username)
    {
        return await work.Connection.QuerySingleOrDefaultAsync<long?>("SELECT id FROM users WHERE username = @username", new { username });
    }

    public async Task Follow(long viewer, long userId)
    {
        await work.Connection.ExecuteAsync("INSERT INTO follows (follower_id, followee_id) VALUES (@viewer, @userId) ON CONFLICT DO NOTHING", new { viewer, userId });
    }

    public async Task Unfollow(long viewer, long userId)
    {
        await work.Connection.ExecuteAsync("DELETE FROM follows WHERE follower_id = @viewer AND followee_id = @userId", new { viewer, userId });
    }
}
