using Sandbox.Common.Commands;
using Sandbox.Common.Database;
using Sandbox.Common.Queries;

namespace Sandbox.Common.Tests.Fakes;

public class TestRunner(IConnectionFactory connectionFactory) : Runner(connectionFactory);

public class NullConnectionFactory : IConnectionFactory
{
    public Task<INotTransactionConnection> CreateConnection() => throw new NotSupportedException();
    public void Dispose() {}
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

public sealed record StockIncreased(int Quantity) : IEvent;

public sealed record IncreaseStock(string ItemId, int Quantity) : ICommand;
public sealed record GetStock(string ItemId) : IQuery<int>;

public sealed record UnhandledCommand : ICommand;
public sealed record UnhandledQuery : IQuery<int>;

public class IncreaseStockHandler(EventBus eventBus) : ICommandHandler<IncreaseStock>
{
    public async Task Handle(IncreaseStock command)
    {
        await eventBus.Dispatch("tenant", command.ItemId, new StockIncreased(command.Quantity));
    }
}

public class StockReadModel : IEventHandler<StockIncreased>, IQueryHandler<GetStock, int>
{
    private readonly Dictionary<string, int> _stocks = new();

    public Task Handle(string tenant, string aggregateId, StockIncreased @event)
    {
        _stocks[aggregateId] = _stocks.GetValueOrDefault(aggregateId) + @event.Quantity;
        return Task.CompletedTask;
    }

    public int Request(GetStock query) => _stocks.GetValueOrDefault(query.ItemId);
}
