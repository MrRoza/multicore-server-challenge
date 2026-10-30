using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Profiles;

public static class UnfollowUser
{
    public record Command(long Viewer, string Username) : ICommand<ProfileResponse>;

    public sealed class Handler(NpgsqlDataSource data, IDispatcher dispatcher) : ICommandHandler<Command, ProfileResponse>
    {
        public async Task<Result<ProfileResponse>> Handle(Command command)
        {
            await using var db = await data.OpenConnectionAsync();
            var id = await db.QuerySingleOrDefaultAsync<long?>("SELECT id FROM users WHERE username = @Username", new { command.Username });
            if (id is null) return Error.NotFound("user not found");

            await db.ExecuteAsync("DELETE FROM follows WHERE follower_id = @Viewer AND followee_id = @id", new { command.Viewer, id });
            return await dispatcher.Query(new GetProfile.Query(command.Viewer, command.Username));
        }
    }
}
