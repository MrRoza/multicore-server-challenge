using Conduit.Profiles.Queries;
using Conduit.Profiles.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Profiles.Commands;

public static class UnfollowUser
{
    public record Command(long Viewer, string Username) : ICommand<ProfileResponseDto>;

    public sealed class Handler(NpgsqlDataSource data, IDispatcher dispatcher) : ICommandHandler<Command, ProfileResponseDto>
    {
        public async Task<Result<ProfileResponseDto>> Handle(Command command)
        {
            await using (var db = await data.OpenConnectionAsync())
            {
                var id = await db.QuerySingleOrDefaultAsync<long?>("SELECT id FROM users WHERE username = @Username", new { command.Username });
                if (id is null) return Error.NotFound("user not found");

                await db.ExecuteAsync("DELETE FROM follows WHERE follower_id = @Viewer AND followee_id = @id", new { command.Viewer, id });
            }

            // After the connection is back in the pool: the query takes its own.
            return await dispatcher.Query(new GetProfile.Query(command.Viewer, command.Username));
        }
    }
}
