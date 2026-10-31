using Conduit.Tags.Responses;
using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Tags.Queries;

public static class ListTags
{
    public record Query : IQuery<ListTagsResponseDto>;

    public sealed class Handler(NpgsqlDataSource data) : IQueryHandler<Query, ListTagsResponseDto>
    {
        public async Task<Result<ListTagsResponseDto>> Handle(Query query)
        {
            await using (var db = await data.OpenConnectionAsync())
            {
                var tags = await db.QueryAsync<string>("SELECT name FROM tags ORDER BY name");
                return new ListTagsResponseDto(tags);
            }
        }
    }
}
