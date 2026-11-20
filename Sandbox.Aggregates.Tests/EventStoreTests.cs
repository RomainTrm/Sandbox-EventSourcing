using System.Text.Json.Serialization;
using Sandbox.Common;
using Sandbox.Common.Database;
using Sandbox.Common.Readmodels;

namespace Sandbox.Aggregates.Tests;

[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record TestEvent : IEvent;
public sealed record ItemAdded(string Name) : TestEvent;
public sealed record ItemRemoved(string Name) : TestEvent;

[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record OrderEvent : IEvent;
public sealed record OrderPlaced(decimal Amount) : OrderEvent;
public sealed record OrderCancelled(string Reason) : OrderEvent;

public class EventStoreTests : IAsyncLifetime
{
    private const string AggregateName = "test-aggregate";

    private ImplicitRollbackTransactionConnectionFactory _factory = null!;
    private INotTransactionConnection _connection = null!;
    private EventStore _store = null!;

    public async Task InitializeAsync()
    {
        _factory = new ImplicitRollbackTransactionConnectionFactory(new DatabaseConfig());
        _connection = (ITransactionConnection)await _factory.CreateConnection();
        _store = new EventStore(_connection, new EventBus([]));
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await ((IAsyncDisposable)_factory).DisposeAsync();
    }

    private static EventStore.Query NewQuery(string? tenantId = null, string aggregateName = AggregateName, string? aggregateId = null) =>
        new(tenantId ?? $"tenant-{Guid.NewGuid()}", aggregateName, aggregateId ?? Guid.NewGuid().ToString());

    [Fact]
    public async Task LoadEvents_UnknownAggregate_ReturnsEmpty()
    {
        var events = await _store.LoadEvents<TestEvent>(NewQuery());

        Assert.Empty(events);
    }

    [Fact]
    public async Task SaveEvents_NewAggregate_Version0_EventsCanBeLoadedBack()
    {
        var query = NewQuery();
        List<TestEvent> newEvents = [new ItemAdded("apple")];

        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, newEvents)]);
        var events = await _store.LoadEvents<TestEvent>(query);

        Assert.Equal([new ItemAdded("apple")], events);
    }

    [Fact]
    public async Task LoadEvents_ReturnsEventsInSavedOrder()
    {
        var query = NewQuery();
        List<TestEvent> newEvents = [new ItemAdded("apple"), new ItemAdded("pear"), new ItemRemoved("apple")];

        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, newEvents)]);
        var events = await _store.LoadEvents<TestEvent>(query);

        Assert.Equal([new ItemAdded("apple"), new ItemAdded("pear"), new ItemRemoved("apple")], events);
    }

    [Fact]
    public async Task SaveEvents_ExistingAggregate_AppendsWithCurrentVersion()
    {
        var query = NewQuery();
        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, [new ItemAdded("apple"), new ItemAdded("pear")])]);

        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 2, [new ItemRemoved("apple")])]);
        var events = await _store.LoadEvents<TestEvent>(query);

        Assert.Equal([new ItemAdded("apple"), new ItemAdded("pear"), new ItemRemoved("apple")], events);
    }

    [Fact]
    public async Task SaveEvents_WithStaleVersion_Throws()
    {
        var query = NewQuery();
        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, [new ItemAdded("apple")])]);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, [new ItemAdded("pear")])]));
    }

    [Fact]
    public async Task SaveEvents_MultipleRequests_SavesEachAggregate()
    {
        var tenantId = $"tenant-{Guid.NewGuid()}";
        var firstQuery = NewQuery(tenantId);
        var secondQuery = NewQuery(tenantId);

        await _store.SaveEvents([
            new EventStore.SaveRequest<TestEvent>(firstQuery, 0, [new ItemAdded("apple")]),
            new EventStore.SaveRequest<TestEvent>(secondQuery, 0, [new ItemAdded("pear"), new ItemRemoved("pear")]),
        ]);

        Assert.Equal([new ItemAdded("apple")], await _store.LoadEvents<TestEvent>(firstQuery));
        Assert.Equal([new ItemAdded("pear"), new ItemRemoved("pear")], await _store.LoadEvents<TestEvent>(secondQuery));
    }

    [Fact]
    public async Task SaveEvents_MultipleRequestsWithDifferentEventTypes_SavesEachAggregate()
    {
        var tenantId = $"tenant-{Guid.NewGuid()}";
        var cart = NewQuery(tenantId, "cart");
        var order = NewQuery(tenantId, "order");

        await _store.SaveEvents([
            new EventStore.SaveRequest<TestEvent>(cart, 0, [new ItemAdded("apple"), new ItemRemoved("apple")]),
            new EventStore.SaveRequest<OrderEvent>(order, 0, [new OrderPlaced(12.5m), new OrderCancelled("out of stock")]),
        ]);

        Assert.Equal([new ItemAdded("apple"), new ItemRemoved("apple")], await _store.LoadEvents<TestEvent>(cart));
        Assert.Equal([new OrderPlaced(12.5m), new OrderCancelled("out of stock")], await _store.LoadEvents<OrderEvent>(order));
    }

    [Fact]
    public async Task LoadEvents_IsolatesByTenant()
    {
        var aggregateId = Guid.NewGuid().ToString();
        var firstTenant = NewQuery(aggregateId: aggregateId);
        var secondTenant = NewQuery(aggregateId: aggregateId);

        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(firstTenant, 0, [new ItemAdded("apple")])]);

        Assert.Equal([new ItemAdded("apple")], await _store.LoadEvents<TestEvent>(firstTenant));
        Assert.Empty(await _store.LoadEvents<TestEvent>(secondTenant));
    }

    [Fact]
    public async Task LoadEvents_IsolatesByAggregateName()
    {
        var tenantId = $"tenant-{Guid.NewGuid()}";
        var aggregateId = Guid.NewGuid().ToString();
        var cart = NewQuery(tenantId, "cart", aggregateId);
        var order = NewQuery(tenantId, "order", aggregateId);

        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(cart, 0, [new ItemAdded("apple")])]);

        Assert.Equal([new ItemAdded("apple")], await _store.LoadEvents<TestEvent>(cart));
        Assert.Empty(await _store.LoadEvents<TestEvent>(order));
    }

    [Fact]
    public async Task SaveEvents_EmptyNewEvents_DoesNothing()
    {
        var query = NewQuery();

        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, [])]);

        Assert.Empty(await _store.LoadEvents<TestEvent>(query));
    }

    [Fact]
    public async Task SaveEvents_DispatchesSavedEventsInOrder()
    {
        var handler = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([handler]));
        var query = NewQuery();

        await store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, [new ItemAdded("apple"), new ItemRemoved("apple")])]);

        Assert.Equal(
            [(query.TenantId, query.AggregateId, new ItemAdded("apple")), (query.TenantId, query.AggregateId, (IEvent)new ItemRemoved("apple"))],
            handler.Received);
    }

    [Fact]
    public async Task SaveEvents_DispatchesToEveryHandlerOfTheSameEventType()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([first, second]));
        var query = NewQuery();

        await store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, [new ItemAdded("apple")])]);

        Assert.Single(first.Received);
        Assert.Single(second.Received);
    }

    [Fact]
    public async Task SaveEvents_WithStaleVersion_DispatchesNothing()
    {
        var query = NewQuery();
        await _store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, [new ItemAdded("apple")])]);
        var handler = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([handler]));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            store.SaveEvents([new EventStore.SaveRequest<TestEvent>(query, 0, [new ItemAdded("pear")])]));

        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task SaveEvents_DoesNotDispatchToBaseTypeHandlers()
    {
        var handler = new BaseTypeHandler();
        var store = new EventStore(_connection, new EventBus([handler]));

        await store.SaveEvents([new EventStore.SaveRequest<TestEvent>(NewQuery(), 0, [new ItemAdded("apple")])]);

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task SaveEvents_MultipleRequests_DispatchesEveryAggregateEvents()
    {
        var handler = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([handler]));
        var tenantId = $"tenant-{Guid.NewGuid()}";
        var firstQuery = NewQuery(tenantId);
        var secondQuery = NewQuery(tenantId);

        await store.SaveEvents([
            new EventStore.SaveRequest<TestEvent>(firstQuery, 0, [new ItemAdded("apple")]),
            new EventStore.SaveRequest<TestEvent>(secondQuery, 0, [new ItemRemoved("pear")]),
        ]);

        Assert.Equal(
            [(tenantId, firstQuery.AggregateId, new ItemAdded("apple")), (tenantId, secondQuery.AggregateId, (IEvent)new ItemRemoved("pear"))],
            handler.Received);
    }
}

public class RecordingHandler : IEventHandler<ItemAdded>, IEventHandler<ItemRemoved>
{
    public List<(string Tenant, string AggregateId, IEvent Event)> Received { get; } = [];

    public void Handle(string tenant, string aggregateId, ItemAdded @event) => Received.Add((tenant, aggregateId, @event));
    public void Handle(string tenant, string aggregateId, ItemRemoved @event) => Received.Add((tenant, aggregateId, @event));
}

public class BaseTypeHandler : IEventHandler<TestEvent>
{
    public int Calls { get; private set; }

    public void Handle(string tenant, string aggregateId, TestEvent @event) => Calls++;
}
