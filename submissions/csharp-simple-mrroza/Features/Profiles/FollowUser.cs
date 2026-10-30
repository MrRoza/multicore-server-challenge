using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Profiles;

public static class FollowUser
{
    public record Command(long Viewer, string Username) : ICommand<ProfileResponse>;

    public sealed class Handler(NpgsqlDataSource data, IDispatcher dispatcher) : ICommandHandler<Command, ProfileResponse>
    {
        public async Task<Result<ProfileResponse>> Handle(Command command)
        {
            await using var db = await data.OpenConnectionAsync();
            var id = await db.QuerySingleOrDefaultAsync<long?>("SELECT id FROM users WHERE username = @Username", new { command.Username });
            if (id is null) return Error.NotFound("user not found");

            await db.ExecuteAsync(
                "INSERT INTO follows (follower_id, followee_id) VALUES (@Viewer, @id) ON CONFLICT DO NOTHING",
                new { command.Viewer, id });
            return await dispatcher.Query(new GetProfile.Query(command.Viewer, command.Username));
        }
    }
}
