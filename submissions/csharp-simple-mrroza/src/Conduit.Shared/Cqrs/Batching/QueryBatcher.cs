using System.Diagnostics;
using Conduit.Shared.Data;

namespace Conduit.Shared.Cqrs.Batching;

/// <summary>
/// Collects the requests for one query type into a batch, then runs each distinct query once and gives its response to
/// every request that asked for it. Query records compare by value, so that is what makes two of them the same.
/// A batch stops taking requests before it runs, so a request always gets a read that started after it arrived. That
/// keeps read-your-own-writes: a client that saw its write commit can't get a response from before that write.
/// </summary>
public sealed class QueryBatcher<TQuery, TResponse, THandler>(IServiceScopeFactory scopes, QueryLoad load, QueryBatchingOptions options)
    where TQuery : notnull, IQuery<TResponse>
    where THandler : class, IQueryHandler<TQuery, TResponse>
{
    readonly Lock gate = new();
    Dictionary<TQuery, TaskCompletionSource<Result<TResponse>>>? open;
    int joined;
    long lastJoinedAt;

    public bool ShouldBatch => options.Enabled && load.Running >= options.HighLoadRunning;

    public Task<Result<TResponse>> Join(TQuery query)
    {
        Task<Result<TResponse>> response;
        Dictionary<TQuery, TaskCompletionSource<Result<TResponse>>> batch;
        bool opened = false, full = false;
        lock (gate)
        {
            if (open is null)
            {
                (open, joined, opened) = ([], 0, true);
            }
            batch = open;
            if (!batch.TryGetValue(query, out var shared))
            {
                batch[query] = shared = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            response = shared.Task;
            lastJoinedAt = Stopwatch.GetTimestamp();
            if (++joined >= options.MaxBatchSize)
            {
                (open, full) = (null, true);
            }
        }

        if (full) RunAll(batch);
        else if (opened) _ = CloseWhenDue(batch);
        return response;
    }

    async Task CloseWhenDue(Dictionary<TQuery, TaskCompletionSource<Result<TResponse>>> batch)
    {
        var openedAt = Stopwatch.GetTimestamp();
        var idle = TimeSpan.FromMilliseconds(options.IdleMs);
        var maxWait = TimeSpan.FromMilliseconds(options.MaxWaitMs);
        while (true)
        {
            TimeSpan wait;
            lock (gate)
            {
                if (open != batch) return; // It filled up first
                wait = TimeSpan.FromTicks(Math.Min(
                    (idle - Stopwatch.GetElapsedTime(lastJoinedAt)).Ticks,
                    (maxWait - Stopwatch.GetElapsedTime(openedAt)).Ticks));
                if (wait <= TimeSpan.Zero)
                {
                    open = null;
                    break;
                }
            }
            await Task.Delay(wait);
        }
        RunAll(batch);
    }

    void RunAll(Dictionary<TQuery, TaskCompletionSource<Result<TResponse>>> batch)
    {
        foreach (var (query, shared) in batch) _ = RunOne(query, shared);
    }

    /// <summary>In its own scope, because each request sharing this response ends its own scope when it finishes.</summary>
    async Task RunOne(TQuery query, TaskCompletionSource<Result<TResponse>> shared)
    {
        try
        {
            await using (var scope = scopes.CreateAsyncScope())
            await using (scope.ServiceProvider.GetRequiredService<IUnitOfWork>().Begin())
            {
                var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                shared.TrySetResult(await load.Run<TQuery, TResponse>(handler, query));
            }
        }
        catch (Exception exception)
        {
            shared.TrySetException(exception);
        }
    }
}
