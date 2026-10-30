using Conduit.Shared;
using Dapper;
using Npgsql;

namespace Conduit.Features.Tags;

public static class ListTags
{
    public record Query : IQuery<Response>;

    public record Response(IEnumerable<string> Tags);

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
