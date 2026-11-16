using System.Data;
using Npgsql;

namespace Sandbox.Common.Database;

public interface IConnectionFactory : IDisposable, IAsyncDisposable
{
    Task<INotTransactionConnection> CreateConnection();
}

public record DatabaseConfig(string Host = "127.0.0.1", int Port = 5432, string Login = "postgres", string Password = "postgres", string Database = "postgres")
{
    public NpgsqlDataSource BuildDataSource()
    {
        var connectionString = new NpgsqlConnectionStringBuilder
        {
            Database = Database,
            Host = Host,
            Port = Port,
            Username = Login,
            Password = Password,
            SslMode = SslMode.Prefer
        }.ConnectionString;

        return new NpgsqlDataSourceBuilder(connectionString).Build();
    }
}

public class ConnectionFactory(DatabaseConfig config) : IConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource = config.BuildDataSource();

    public Task<INotTransactionConnection> CreateConnection()
    {
        var notTransactionDatabaseConnection = new NotTransactionDatabaseConnection(_dataSource.CreateConnection());
        return Task.FromResult<INotTransactionConnection>(notTransactionDatabaseConnection);
    }

    void IDisposable.Dispose() => _dataSource.Dispose();
    async ValueTask IAsyncDisposable.DisposeAsync() => await _dataSource.DisposeAsync();
}

public class ImplicitRollbackTransactionConnectionFactory(DatabaseConfig config) : IConnectionFactory
{
    private readonly NpgsqlDataSource _dataSource = config.BuildDataSource();
    
    public async Task<INotTransactionConnection> CreateConnection()
    {
        var connection = new NotTransactionDatabaseConnection(_dataSource.CreateConnection());
        await connection.NpgsqlConnection.OpenAsync();
        var transaction = await connection.NpgsqlConnection.BeginTransactionAsync(IsolationLevel.RepeatableRead);
        return new TransactionDatabaseConnection(connection.NpgsqlConnection, transaction, true);
    }

    void IDisposable.Dispose() => _dataSource.Dispose();
    async ValueTask IAsyncDisposable.DisposeAsync() => await _dataSource.DisposeAsync();
}
