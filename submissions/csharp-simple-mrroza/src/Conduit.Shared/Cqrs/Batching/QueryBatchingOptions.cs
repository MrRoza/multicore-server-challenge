namespace Conduit.Shared.Cqrs.Batching;

/// <summary>The `QueryBatching` section of appsettings.json. AddQuery can overrule these for one query.</summary>
public sealed record QueryBatchingOptions
{
    public bool Enabled { get; init; } = true;

    /// <summary>A batch closes at once when this many requests have joined it.</summary>
    public int MaxBatchSize { get; init; } = 5000;

    /// <summary>A batch closes when no request has joined it for this long, so a lone request doesn't wait for long.</summary>
    public int IdleMs { get; init; } = 50;

    /// <summary>A batch closes this long after it opened, however many requests are still joining it.</summary>
    public int MaxWaitMs { get; init; } = 200;

    /// <summary>
    /// Requests only wait in a batch when this many queries are running. The default matches start.sh's connection pool,
    /// so batching starts once queries would queue for a connection.
    /// </summary>
    public int HighLoadRunning { get; init; } = 90;
}
