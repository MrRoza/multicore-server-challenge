using Npgsql;

namespace Conduit.Shared.Data.Postgres;

/// <summary>
/// Takes a connection from the pool on the first query, not when the work begins, so work that never reaches the
/// database doesn't hold one. Statements on a connection with an open transaction run inside it.
/// </summary>
public sealed class PostgresUnitOfWork(NpgsqlDataSource data) : IUnitOfWork
{
    bool begun;
    bool useTransaction;
    NpgsqlConnection? connection;
    NpgsqlTransaction? transaction;

    public IUnitOfWork Begin()
    {
        begun = true;
        return this;
    }

    public IUnitOfWork BeginTransaction()
    {
        useTransaction = true;
        return Begin();
    }

    public async Task<NpgsqlConnection> GetConnectionAsync()
    {
        if (!begun) throw new InvalidOperationException("the unit of work hasn't begun");
        if (connection is null)
        {
            connection = await data.OpenConnectionAsync();
            if (useTransaction) transaction = await connection.BeginTransactionAsync();
        }
        return connection;
    }

    public async Task Commit()
    {
        if (!useTransaction) throw new InvalidOperationException("there is no transaction to commit");
        if (transaction is not null) await transaction.CommitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null) await transaction.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        (transaction, connection) = (null, null);
    }
}
