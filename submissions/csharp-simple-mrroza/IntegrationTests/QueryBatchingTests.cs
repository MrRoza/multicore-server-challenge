using Conduit.Shared;
using Conduit.Shared.Cqrs;
using Conduit.Shared.Cqrs.Batching;
using Conduit.Shared.Data;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IntegrationTests;

/// <summary>The batcher on its own, with a handler that counts its runs. No server or database.</summary>
public sealed class QueryBatchingTests
{
    const int Never = 60_000;

    public record Echo(string Key) : IQuery<string>;

    public sealed class Runs
    {
        public int Count;
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public sealed class Handler(Runs runs) : IQueryHandler<Echo, string>
    {
        public async Task<Result<string>> Handle(Echo query)
        {
            var run = Interlocked.Increment(ref runs.Count);
            runs.Started.TrySetResult();
            await runs.Release.Task;
            if (query.Key == "fail") return Error.NotFound("failed");
            return $"{query.Key}#{run}";
        }
    }

    public sealed class FakeWork : IUnitOfWork
    {
        public bool InTransaction { get; private set; }

        public IUnitOfWork Begin() => this;

        public IUnitOfWork BeginTransaction()
        {
            InTransaction = true;
            return this;
        }

        public Task Commit() => Task.CompletedTask;

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    readonly Runs runs = new();

    IServiceProvider Services(int size = 5000, int idleMs = Never, int maxWaitMs = Never, int highLoad = 0, Func<QueryBatchingOptions, QueryBatchingOptions>? overrule = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MaxBatchSize"] = size.ToString(),
            ["IdleMs"] = idleMs.ToString(),
            ["MaxWaitMs"] = maxWaitMs.ToString(),
            ["HighLoadRunning"] = highLoad.ToString(),
        }).Build();
        return new ServiceCollection()
            .AddSingleton(runs)
            .AddScoped<IUnitOfWork, FakeWork>()
            .AddQueryBatching(configuration)
            .AddQuery<Echo, string, Handler>(overrule)
            .BuildServiceProvider();
    }

    static Task<Result<string>> Send(IServiceProvider services, string key) =>
        services.CreateScope().ServiceProvider.GetRequiredService<IQueryHandler<Echo, string>>().Handle(new Echo(key));

    [Fact]
    public async Task Duplicates_in_a_batch_run_once_and_share_the_response()
    {
        var services = Services(size: 3);
        runs.Release.SetResult();

        var responses = await Task.WhenAll(Send(services, "a"), Send(services, "a"), Send(services, "a"));

        Assert.Equal(1, runs.Count);
        Assert.All(responses, response => Assert.Equal("a#1", response.Unwrap()));
    }

    [Fact]
    public async Task Different_queries_in_a_batch_each_run()
    {
        var services = Services(size: 2);
        runs.Release.SetResult();

        var responses = await Task.WhenAll(Send(services, "a"), Send(services, "b"));

        Assert.Equal(2, runs.Count);
        Assert.StartsWith("a#", responses[0].Unwrap());
        Assert.StartsWith("b#", responses[1].Unwrap());
    }

    [Fact]
    public async Task A_lone_request_runs_once_the_batch_is_idle()
    {
        var services = Services(idleMs: 10);
        runs.Release.SetResult();

        Assert.Equal("a#1", (await Send(services, "a")).Unwrap());
    }

    [Fact]
    public async Task A_new_request_keeps_the_batch_open()
    {
        var services = Services(idleMs: 400);
        runs.Release.SetResult();

        var first = Send(services, "a");
        await Task.Delay(250);
        var second = Send(services, "a");
        await Task.Delay(250); // 500 ms since the first request, but only 250 ms since the last
        Assert.Equal(0, runs.Count);

        await Task.WhenAll(first, second);
        Assert.Equal(1, runs.Count);
    }

    [Fact]
    public async Task The_max_wait_closes_a_batch_that_keeps_getting_requests()
    {
        var services = Services(maxWaitMs: 10);
        runs.Release.SetResult();

        Assert.Equal("a#1", (await Send(services, "a")).Unwrap());
    }

    [Fact]
    public async Task A_query_arriving_after_the_run_started_gets_its_own_run()
    {
        var services = Services(size: 1);

        var first = Send(services, "a");
        await runs.Started.Task;
        var second = Send(services, "a");
        runs.Release.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(2, runs.Count);
    }

    [Fact]
    public async Task Errors_are_shared_too()
    {
        var services = Services(size: 2);
        runs.Release.SetResult();

        var responses = await Task.WhenAll(Send(services, "fail"), Send(services, "fail"));

        Assert.Equal(1, runs.Count);
        Assert.All(responses, response => Assert.Throws<ConduitException>(() => response.Unwrap()));
    }

    [Fact]
    public async Task A_query_in_a_transaction_runs_directly()
    {
        var scope = Services().CreateScope().ServiceProvider;
        scope.GetRequiredService<IUnitOfWork>().BeginTransaction();
        runs.Release.SetResult();

        var response = await scope.GetRequiredService<IQueryHandler<Echo, string>>().Handle(new Echo("a"));

        Assert.Equal("a#1", response.Unwrap());
    }

    [Fact]
    public async Task A_query_can_overrule_the_global_options()
    {
        var services = Services(overrule: options => options with { Enabled = false });
        runs.Release.SetResult();

        Assert.Equal("a#1", (await Send(services, "a")).Unwrap());
    }

    [Fact]
    public async Task Batching_only_starts_under_high_load()
    {
        var services = Services(highLoad: 1, size: 2);

        var running = Send(services, "direct"); // Nothing is running yet, so this one goes straight through
        await runs.Started.Task;
        var waiting = Send(services, "a");
        await Task.Delay(50);
        Assert.Equal(1, runs.Count);
        Assert.False(waiting.IsCompleted);

        var last = Send(services, "a");
        runs.Release.SetResult();
        await Task.WhenAll(running, waiting, last);

        Assert.Equal(2, runs.Count);
    }
}
