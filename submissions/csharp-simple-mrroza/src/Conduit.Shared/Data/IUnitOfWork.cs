namespace Conduit.Shared.Data;

/// <summary>One database connection for one request. Whoever receives the request begins it, and repositories use it.</summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>Begins work that only reads.</summary>
    IUnitOfWork Begin();

    /// <summary>Begins work in a transaction. Without a Commit, disposing rolls it back.</summary>
    IUnitOfWork BeginTransaction();

    Task Commit();
}
