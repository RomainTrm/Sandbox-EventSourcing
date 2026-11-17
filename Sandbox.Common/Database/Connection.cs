using System.Data;
using Npgsql;
using Dapper;

namespace Sandbox.Common.Database;

public interface IConnection
{
    Task Execute(string sqlQuery, IEnumerable<KeyValuePair<string, object>> parameters);
    Task ExecuteAll(string sqlQuery, IEnumerable<IEnumerable<KeyValuePair<string, object>>> parameters);
    Task<List<TResult>> QueryAll<TResult>(string sqlQuery, IEnumerable<KeyValuePair<string, object>> parameters);
    Task<TResult> QueryFirst<TResult>(string sqlQuery, IEnumerable<KeyValuePair<string, object>> parameters);
}

public interface INotTransactionConnection : IConnection, IDisposable, IAsyncDisposable
{
    Task<ITransactionConnection> BeginTransaction();
}

public interface ITransactionConnection : INotTransactionConnection
{
    Task Commit();
    Task Rollback();
}

public abstract class DatabaseConnection(NpgsqlConnection connection) : IConnection
{
    public NpgsqlConnection NpgsqlConnection { get; } = connection;
    
    public async Task Execute(string sqlQuery, IEnumerable<KeyValuePair<string, object>> parameters)
    {
        await NpgsqlConnection.ExecuteAsync(sqlQuery, parameters);
    }

    public async Task ExecuteAll(string sqlQuery, IEnumerable<IEnumerable<KeyValuePair<string, object>>> parameters)
    {
        await NpgsqlConnection.ExecuteAsync(sqlQuery, parameters);
    }

    public async Task<List<TResult>> QueryAll<TResult>(string sqlQuery, IEnumerable<KeyValuePair<string, object>> parameters)
    {
        var result = await NpgsqlConnection.QueryAsync<TResult>(sqlQuery, parameters);
        return result.ToList();
    }

    public async Task<TResult> QueryFirst<TResult>(string sqlQuery, IEnumerable<KeyValuePair<string, object>> parameters)
    {
        return await NpgsqlConnection.QueryFirstAsync<TResult>(sqlQuery, parameters);
    }
}

public class NotTransactionDatabaseConnection(NpgsqlConnection connection) : DatabaseConnection(connection), INotTransactionConnection
{
    public async Task<ITransactionConnection> BeginTransaction()
    {
        if (NpgsqlConnection.State != ConnectionState.Open) await NpgsqlConnection.OpenAsync();
        
        var transaction = await NpgsqlConnection.BeginTransactionAsync();
        return new TransactionDatabaseConnection(NpgsqlConnection, transaction, false);
    }
    
    void IDisposable.Dispose() => NpgsqlConnection.Dispose();
    async ValueTask IAsyncDisposable.DisposeAsync() => await NpgsqlConnection.DisposeAsync();
}

public class TransactionDatabaseConnection(NpgsqlConnection connection, NpgsqlTransaction transaction, bool disposeConnection) : NotTransactionDatabaseConnection(connection), ITransactionConnection
{
    public Task Commit() => transaction.CommitAsync();
    public Task Rollback() => transaction.RollbackAsync();
    
    void IDisposable.Dispose()
    {
        transaction.Dispose();
        if (disposeConnection) NpgsqlConnection.Dispose();
    }

    async ValueTask IAsyncDisposable.DisposeAsync()
    {
        await transaction.DisposeAsync();
        if (disposeConnection) await NpgsqlConnection.DisposeAsync();
    }
}
