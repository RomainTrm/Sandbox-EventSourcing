using Sandbox.Common.Readmodels;

namespace Sandbox.Common.Tests.Fakes;

public closed record TestEvent : IEvent;
public sealed record ItemAdded(string Name) : TestEvent;
public sealed record ItemRemoved(string Name) : TestEvent;

public sealed record OrderPlaced(decimal Amount) : IEvent;

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

public class ThrowingHandler : IEventHandler<OrderPlaced>
{
    public void Handle(string tenant, string aggregateId, OrderPlaced @event) => throw new InvalidOperationException();
}
