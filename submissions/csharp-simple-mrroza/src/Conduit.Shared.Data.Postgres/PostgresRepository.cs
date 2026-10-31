using Dapper;

namespace Conduit.Shared.Data.Postgres;

/// <summary>Runs SQL on the request's unit of work. This is the only place that asks for its connection.</summary>
public abstract class PostgresRepository(PostgresUnitOfWork work)
{
    protected async Task<IEnumerable<T>> Query<T>(string sql, object? args = null) =>
        await (await work.GetConnectionAsync()).QueryAsync<T>(sql, args);

    protected async Task<T> QuerySingle<T>(string sql, object? args = null) =>
        await (await work.GetConnectionAsync()).QuerySingleAsync<T>(sql, args);

    protected async Task<T?> QuerySingleOrDefault<T>(string sql, object? args = null) =>
        await (await work.GetConnectionAsync()).QuerySingleOrDefaultAsync<T>(sql, args);

    protected async Task<T> ExecuteScalar<T>(string sql, object? args = null) =>
        (await (await work.GetConnectionAsync()).ExecuteScalarAsync<T>(sql, args))!;

    protected async Task Execute(string sql, object? args = null) =>
        await (await work.GetConnectionAsync()).ExecuteAsync(sql, args);
}
