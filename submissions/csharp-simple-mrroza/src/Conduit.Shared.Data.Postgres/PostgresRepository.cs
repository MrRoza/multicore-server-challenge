using Conduit.Shared.Cqrs;
using Dapper;
using Npgsql;

namespace Conduit.Shared.Data.Postgres;

/// <summary>Runs SQL on the request's unit of work. This is the only place that asks for its connection.</summary>
public abstract class PostgresRepository(PostgresUnitOfWork work)
{
    protected Task<IEnumerable<T>> Query<T>(string sql, object? args = null) =>
        Run(connection => connection.QueryAsync<T>(sql, args));

    /// <summary>Runs two queries in one round trip to the database.</summary>
    protected Task<(IEnumerable<TFirst> First, IEnumerable<TSecond> Second)> QueryBoth<TFirst, TSecond>(string firstSql, string secondSql, object? args = null) =>
        Run(async connection =>
        {
            await using (var results = await connection.QueryMultipleAsync(firstSql + ";\n" + secondSql, args))
            {
                var first = await results.ReadAsync<TFirst>();
                var second = await results.ReadAsync<TSecond>();
                return (first, second);
            }
        });

    protected Task<T> QuerySingle<T>(string sql, object? args = null) =>
        Run(connection => connection.QuerySingleAsync<T>(sql, args));

    protected Task<T?> QuerySingleOrDefault<T>(string sql, object? args = null) =>
        Run(connection => connection.QuerySingleOrDefaultAsync<T>(sql, args));

    protected Task<T> ExecuteScalar<T>(string sql, object? args = null) =>
        Run(async connection => (await connection.ExecuteScalarAsync<T>(sql, args))!);

    protected Task Execute(string sql, object? args = null) =>
        Run(connection => connection.ExecuteAsync(sql, args));

    async Task<T> Run<T>(Func<NpgsqlConnection, Task<T>> statement)
    {
        try
        {
            return await statement(await work.GetConnectionAsync());
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.UniqueViolation)
        {
            throw new ConduitException(Error.Validation("that value is already taken"));
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.CharacterNotInRepertoire)
        {
            throw new ConduitException(Error.Validation("the text is invalid"));
        }
        catch (PostgresException e) when (e.SqlState is PostgresErrorCodes.ForeignKeyViolation)
        {
            throw new ConduitException(Error.NotFound("it no longer exists"));
        }
    }
}
