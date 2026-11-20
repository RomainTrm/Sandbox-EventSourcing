using System.Data;
using Npgsql;

namespace Sandbox.Common.Database;

public class NotDisposableDatabaseConnection(TransactionDatabaseConnection connection) : DatabaseConnection(connection.NpgsqlConnection), ITransactionConnection, INotTransactionConnection
{
    private bool Disposed { get; set; }
    private List<NotDisposableDatabaseConnection> SubConnections { get; } = [];

    void IDisposable.Dispose()
    {
        SubConnections.ForEach(subConnection => subConnection.EnsureAllDisposed());
        Disposed = true;
    }

    ValueTask IAsyncDisposable.DisposeAsync()
    {
        ((IDisposable)this).Dispose();
        return ValueTask.CompletedTask;
    }

    public Task<ITransactionConnection> BeginTransaction()
    {
        AssertDispose();
        var newConnection = new NotDisposableDatabaseConnection(connection);
        SubConnections.Add(newConnection);
        return Task.FromResult<ITransactionConnection>(newConnection);
    }

    public Task Commit()
    {
        AssertDispose();
        return Task.CompletedTask;
    }

    public Task Rollback()
    {
        throw new NotImplementedException();
    }

    private void EnsureAllDisposed()
    {
        SubConnections.ForEach(subConnection => subConnection.EnsureAllDisposed());
        if (!Disposed) throw new Exception("Connection not disposed");
    }

    private void AssertDispose()
    {
        if (Disposed) throw new Exception("Connection already disposed");
    }
}

public class ImplicitRollbackTransactionConnectionFactory(DatabaseConfig config) : IDisposable, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource = config.BuildDataSource();
    
    public async Task<NotDisposableDatabaseConnection> CreateConnection()
    {
        var connection = new NotTransactionDatabaseConnection(_dataSource.CreateConnection());
        await connection.NpgsqlConnection.OpenAsync();
        var transaction = await connection.NpgsqlConnection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        var transactionConnection = new TransactionDatabaseConnection(connection.NpgsqlConnection, transaction, true);
        return new NotDisposableDatabaseConnection(transactionConnection);
    }

    void IDisposable.Dispose() => _dataSource.Dispose();
    async ValueTask IAsyncDisposable.DisposeAsync() => await _dataSource.DisposeAsync();
}
