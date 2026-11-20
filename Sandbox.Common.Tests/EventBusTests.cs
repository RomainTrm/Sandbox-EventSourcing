using Sandbox.Common.Tests.Fakes;

namespace Sandbox.Common.Tests;

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
            [("tenant", "id", new ItemAdded("apple")), ("tenant", "id", new ItemRemoved("apple"))],
            handler.Received);
    }

    [Fact]
    public void Dispatch_SeveralHandlersOfTheSameEventType_AllReceiveIt()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        var bus = new EventBus([first, second]);

        bus.Dispatch("tenant", "id", new ItemAdded("apple"));

        Assert.Single(first.Received);
        Assert.Single(second.Received);
    }

    [Fact]
    public void Dispatch_BaseTypeHandler_IsNotCalled()
    {
        var handler = new BaseTypeHandler();
        var bus = new EventBus([handler]);

        bus.Dispatch("tenant", "id", new ItemAdded("apple"));

        Assert.Equal(0, handler.Calls);
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
}
