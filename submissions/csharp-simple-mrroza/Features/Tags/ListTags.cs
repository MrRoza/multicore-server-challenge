using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Tags;

public sealed class ListTags : IEndpoint
{
    public record Query;

    public record Response(IEnumerable<string> Tags);

    public void Map(IEndpointRouteBuilder api) =>
        api.MapGet("/tags", async (IQueryHandler<Query, Response> handler) =>
            (await handler.Handle(new Query())).ToHttp(Results.Ok));

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, Response>
    {
        public async Task<Result<Response>> Handle(Query query)
        {
            await using var db = await data.OpenConnectionAsync();
            var tags = await db.QueryAsync<string>("SELECT name FROM tags ORDER BY name");
            return new Response(tags);
        }
    }
}
