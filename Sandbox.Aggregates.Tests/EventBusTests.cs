using Sandbox.Common;

namespace Sandbox.Aggregates.Tests;

public class EventBusTests
{
    [Fact]
    public void Dispatch_HandlerImplementingSeveralInterfaces_ReceivesEachEventType()
    {
        var handler = new RecordingHandler();
        var bus = new EventBus([handler]);

        bus.Dispatch("tenant", "id", new ItemAdded("apple"));
        bus.Dispatch("tenant", "id", new ItemRemoved("apple"));

        Assert.Equal(
            [("tenant", "id", new ItemAdded("apple")), ("tenant", "id", (IEvent)new ItemRemoved("apple"))],
            handler.Received);
    }

    [Fact]
    public void Dispatch_EventWithoutHandler_DoesNothing()
    {
        var handler = new RecordingHandler();
        var bus = new EventBus([handler]);

        bus.Dispatch("tenant", "id", new OrderPlaced(12.5m));

        Assert.Empty(handler.Received);
    }

    [Fact]
    public void Dispatch_HandlerThrows_ExceptionPropagates()
    {
        var bus = new EventBus([new ThrowingHandler()]);

        Assert.Throws<InvalidOperationException>(() => bus.Dispatch("tenant", "id", new OrderPlaced(1m)));
    }

    private class ThrowingHandler : Sandbox.Common.Readmodels.IEventHandler<OrderPlaced>
    {
        public void Handle(string tenant, string aggregateId, OrderPlaced @event) => throw new InvalidOperationException();
    }
}
