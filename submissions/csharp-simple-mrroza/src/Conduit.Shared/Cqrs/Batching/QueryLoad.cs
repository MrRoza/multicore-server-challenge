namespace Conduit.Shared.Cqrs.Batching;

/// <summary>
/// Counts the queries running right now. Requests waiting in a batch don't count: they cost the database nothing, and
/// counting them would keep the load looking high because of the batching itself.
/// </summary>
public sealed class QueryLoad
{
    int running;

    public int Running => Volatile.Read(ref running);

    public async Task<Result<TResponse>> Run<TQuery, TResponse>(IQueryHandler<TQuery, TResponse> handler, TQuery query)
        where TQuery : IQuery<TResponse>
    {
        Interlocked.Increment(ref running);
        try
        {
            return await handler.Handle(query);
        }
        finally
        {
            Interlocked.Decrement(ref running);
        }
    }
}
