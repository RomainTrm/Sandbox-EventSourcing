using System.Text.Json.Serialization;
using Sandbox.Common;
using Sandbox.Common.Queries;

namespace Sandbox.Aggregates.Tests.Fakes;

[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record TestEvent : IEvent;
public sealed record ItemAdded(string Name) : TestEvent;
public sealed record ItemRemoved(string Name) : TestEvent;

[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record OrderEvent : IEvent;
public sealed record OrderPlaced(decimal Amount) : OrderEvent;
public sealed record OrderCancelled(string Reason) : OrderEvent;

public class RecordingHandler : IEventHandler<ItemAdded>, IEventHandler<ItemRemoved>
{
    public List<(string Tenant, string AggregateId, IEvent Event)> Received { get; } = [];

    public Task Handle(string tenant, string aggregateId, ItemAdded @event)
    {
        Received.Add((tenant, aggregateId, @event));
        return Task.CompletedTask;
    }
    
    public Task Handle(string tenant, string aggregateId, ItemRemoved @event)
    {
        Received.Add((tenant, aggregateId, @event));
        return Task.CompletedTask;
    }
}

public class BaseTypeHandler : IEventHandler<TestEvent>
{
    public int Calls { get; private set; }

    public Task Handle(string tenant, string aggregateId, TestEvent @event)
    {
        Calls++;
        return Task.CompletedTask;
    }
}
