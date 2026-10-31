using Conduit.Shared.Data;
using Conduit.Tags.Abstractions;
using Dapper;

namespace Conduit.Tags.Data.Postgres;

public sealed class TagRepository(IUnitOfWork work) : ITagRepository
{
    public async Task<IEnumerable<string>> List()
    {
        return await work.Connection.QueryAsync<string>("SELECT name FROM tags ORDER BY name");
    }
}
