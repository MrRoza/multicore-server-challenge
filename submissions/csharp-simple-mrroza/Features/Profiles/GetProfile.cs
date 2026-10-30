using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Profiles;

public sealed class GetProfile : IEndpoint
{
    public record Query(long Viewer, string Username);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/profiles/{username}", async (string username, ClaimsPrincipal principal, IQueryHandler<Query, Profile> handler) =>
            (await handler.Handle(new Query(principal.UserId(), username)))
                .ToHttp(profile => Results.Ok(new ProfileResponse(profile))))
            .AuthOptional();

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, Profile>
    {
        public async Task<Result<Profile>> Handle(Query query)
        {
            await using var db = await data.OpenConnectionAsync();
            var row = await db.QuerySingleOrDefaultAsync<ProfileRow>("""
                SELECT u.id, u.username, u.bio, u.image,
                       EXISTS (SELECT 1 FROM follows fo WHERE fo.follower_id = @Viewer AND fo.followee_id = u.id) AS following
                  FROM users u WHERE u.username = @Username
                """, new { query.Viewer, query.Username });
            if (row is null) return Error.NotFound("user not found");
            return row.ToProfile();
        }
    }
}
