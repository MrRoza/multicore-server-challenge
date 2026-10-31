using Conduit.Profiles.Responses;
using Conduit.Shared.Contracts;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Profiles.Queries;

public static class GetProfile
{
    public record Query(long Viewer, string Username) : IQuery<ProfileResponseDto>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ProfileResponseDto>
    {
        public async Task<Result<ProfileResponseDto>> Handle(Query query)
        {
            await using (var db = await data.OpenConnectionAsync())
            {
                var row = await db.QuerySingleOrDefaultAsync<ProfileRowDto>("""
                    SELECT u.id, u.username, u.bio, u.image,
                           EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @Viewer AND fo.followee_id = u.id) AS following
                      FROM users u WHERE u.username = @Username
                    """, new { query.Viewer, query.Username });
                if (row is null) return Error.NotFound("user not found");
                return new ProfileResponseDto(row.ToProfile());
            }
        }
    }
}
