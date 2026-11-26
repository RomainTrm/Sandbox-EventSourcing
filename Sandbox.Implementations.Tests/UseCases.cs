using Sandbox.Common.Database;

namespace Sandbox.Implementations.Tests;

public class UseCases : IAsyncLifetime
{
    private ImplicitRollbackTransactionConnectionFactory _factory = null!;
    private NotDisposableDatabaseConnection _connection = null!;

    public async Task InitializeAsync()
    {
        _factory = new ImplicitRollbackTransactionConnectionFactory(new DatabaseConfig());
        _connection = await _factory.CreateConnection();
    }

    public async Task DisposeAsync()
    {
        await ((IAsyncDisposable)_connection).DisposeAsync();
        await ((IAsyncDisposable)_factory).DisposeAsync();
    }
    
    [Theory, ClassData(typeof(Runners))]
    public async Task Test1(Runners.TestCase testCase)
    {
        var runner = testCase.Factory(_connection);
        await Task.CompletedTask;
    }
}