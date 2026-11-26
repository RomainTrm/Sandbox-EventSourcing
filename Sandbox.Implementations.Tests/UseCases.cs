using Sandbox.Common.Commands;
using Sandbox.Common.Database;
using Sandbox.Common.Dsl;
using Sandbox.Common.Queries;

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

    private readonly StoreId _store1 = new("store1");
    private readonly StoreId _store2 = new("store2");
    
    private readonly OrderId _orderId1 = new("order1");
    private readonly OrderId _orderId2 = new("order2");

    private readonly ItemId _itemId1 = new("item1"); 
    private readonly ItemId _itemId2 = new("item2"); 
    private readonly ItemId _itemId3 = new("item3"); 
    private readonly ItemId _itemId4 = new("item4"); 
    
    [Theory, ClassData(typeof(Runners))]
    public async Task BuyNewItemsOnStore(Runners.TestCase testCase)
    {
        var runner = testCase.Factory(_connection);

        await runner.RunCommand(new CreateBuyingOrder(_store1, _orderId1, [
            new NewItem(_itemId1, "St1Or1It1", 15m),
            new NewItem(_itemId2, "St1Or1It2", 25m),
        ]));
        
        await runner.RunCommand(new CreateBuyingOrder(_store1, _orderId2, [
            new NewItem(_itemId3, "St1Or2It3", 5m),
            new NewItem(_itemId4, "St1Or2It4", 10m),
        ]));
        
        await runner.RunCommand(new CreateBuyingOrder(_store2, _orderId1, [
            new NewItem(_itemId1, "St2Or1It1", 50m),
        ]));

        var result = runner.Request<GetAvailableItems, List<Item>>(new GetAvailableItems(_store1));
        Assert.Equal([
            new Item(_itemId1, "St1Or1It1", 15m),
            new Item(_itemId2, "St1Or1It2", 25m),
            new Item(_itemId3, "St1Or2It3", 5m),
            new Item(_itemId4, "St1Or2It4", 10m),
        ], result);
    }
}