using Npgsql;

namespace Conduit.Shared.Data;

/// <summary>One connection for one request. Whoever receives the request opens it, and repositories use it.</summary>
public interface IUnitOfWork : IAsyncDisposable
{
    NpgsqlConnection Connection { get; }

    /// <summary>Opens the connection, for work that only reads.</summary>
    Task<IUnitOfWork> Begin();

    /// <summary>Opens the connection and a transaction. Without a Commit, disposing rolls it back.</summary>
    Task<IUnitOfWork> BeginTransaction();

    Task Commit();
}
