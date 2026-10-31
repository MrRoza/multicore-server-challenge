using Conduit.Shared.Data.Postgres;
using Conduit.Tags.Abstractions;

namespace Conduit.Tags.Data.Postgres;

public sealed class TagRepository(PostgresUnitOfWork work) : PostgresRepository(work), ITagRepository
{
    public async Task<IEnumerable<string>> List()
    {
        return await Query<string>("SELECT name FROM tags ORDER BY name");
    }
}
