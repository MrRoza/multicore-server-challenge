using Conduit.Shared.Data;

namespace Conduit.Shared.Cqrs.Batching;

/// <summary>
/// Sits in front of a query handler and sends the query through its batcher when load is high. A query inside a
/// transaction always runs directly: it has to see that transaction's uncommitted writes, on that connection.
/// </summary>
public sealed class BatchedQueryHandler<TQuery, TResponse, THandler>(
    THandler handler,
    IUnitOfWork work,
    QueryBatcher<TQuery, TResponse, THandler> batcher,
    QueryLoad load) : IQueryHandler<TQuery, TResponse>
    where TQuery : notnull, IQuery<TResponse>
    where THandler : class, IQueryHandler<TQuery, TResponse>
{
    public Task<Result<TResponse>> Handle(TQuery query) =>
        !work.InTransaction && batcher.ShouldBatch ? batcher.Join(query) : load.Run<TQuery, TResponse>(handler, query);
}
