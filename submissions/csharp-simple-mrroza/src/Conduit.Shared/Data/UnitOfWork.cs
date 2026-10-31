using Npgsql;

namespace Conduit.Shared.Data;

// Statements on a connection with an open transaction run inside it, so repositories don't have to pass it along.
public sealed class UnitOfWork(NpgsqlDataSource data) : IUnitOfWork
{
    NpgsqlConnection? connection;
    NpgsqlTransaction? transaction;

    public NpgsqlConnection Connection => connection ?? throw new InvalidOperationException("the unit of work hasn't begun");

    public async Task<IUnitOfWork> Begin()
    {
        connection = await data.OpenConnectionAsync();
        return this;
    }

    public async Task<IUnitOfWork> BeginTransaction()
    {
        await Begin();
        transaction = await Connection.BeginTransactionAsync();
        return this;
    }

    public async Task Commit()
    {
        if (transaction is null) throw new InvalidOperationException("there is no transaction to commit");
        await transaction.CommitAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (transaction is not null) await transaction.DisposeAsync();
        if (connection is not null) await connection.DisposeAsync();
        (transaction, connection) = (null, null);
    }
}
