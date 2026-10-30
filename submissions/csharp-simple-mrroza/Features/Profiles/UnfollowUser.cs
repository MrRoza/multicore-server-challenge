using System.Security.Claims;
using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Profiles;

public sealed class UnfollowUser : IEndpoint
{
    public record Command(long Viewer, string Username);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapDelete("/profiles/{username}/follow", async (string username, ClaimsPrincipal principal,
            ICommandHandler<Command, Unit> handler, IQueryHandler<GetProfile.Query, Profile> getProfile) =>
            (await handler.Handle(new Command(principal.UserId(), username))
                .Then(_ => getProfile.Handle(new GetProfile.Query(principal.UserId(), username))))
                .ToHttp(profile => Results.Ok(new ProfileResponse(profile))))
            .AuthRequired();

    public sealed class Handler(NpgsqlDataSource data) : ICommandHandler<Command, Unit>
    {
        public async Task<Result<Unit>> Handle(Command command)
        {
            await using var db = await data.OpenConnectionAsync();
            var id = await db.QuerySingleOrDefaultAsync<long?>("SELECT id FROM users WHERE username = @Username", new { command.Username });
            if (id is null) return Error.NotFound("user not found");

            await db.ExecuteAsync("DELETE FROM follows WHERE follower_id = @Viewer AND followee_id = @id", new { command.Viewer, id });
            return new Unit();
        }
    }
}
