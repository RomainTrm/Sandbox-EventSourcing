using System.Text.Json.Serialization;
using Sandbox.Common;
using Sandbox.Common.Readmodels;

namespace Sandbox.DCB.Full.Tests.Fakes;

[JsonPolymorphic(InferClosedTypePolymorphism = true)]
public closed record TestEvent : IEvent;
public sealed record ItemAdded(string Name) : TestEvent;
public sealed record ItemRemoved(string Name) : TestEvent;
public sealed record ItemTransferred(string From, string To) : TestEvent;

public class RecordingHandler : IEventHandler<ItemAdded>, IEventHandler<ItemRemoved>, IEventHandler<ItemTransferred>
{
    public List<(string Tenant, string AggregateId, IEvent Event)> Received { get; } = [];

    public void Handle(string tenant, string aggregateId, ItemAdded @event) => Received.Add((tenant, aggregateId, @event));
    public void Handle(string tenant, string aggregateId, ItemRemoved @event) => Received.Add((tenant, aggregateId, @event));
    public void Handle(string tenant, string aggregateId, ItemTransferred @event) => Received.Add((tenant, aggregateId, @event));
}

public class BaseTypeHandler : IEventHandler<TestEvent>
{
    public int Calls { get; private set; }

    public void Handle(string tenant, string aggregateId, TestEvent @event) => Calls++;
}
