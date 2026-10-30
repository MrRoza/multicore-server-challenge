using Conduit.Models;
using Dapper;
using Npgsql;

namespace Conduit.Endpoints;

public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder api) => api.MapGet("/tags", List);

    static async Task<IResult> List(NpgsqlDataSource data)
    {
        await using var db = await data.OpenConnectionAsync();
        var tags = await db.QueryAsync<string>("SELECT name FROM tags ORDER BY name");
        return Results.Ok(new TagListResponse(tags));
    }
}
