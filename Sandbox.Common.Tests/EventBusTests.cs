using Sandbox.Common.Tests.Fakes;

namespace Sandbox.Common.Tests;

public class EventBusTests
{
    [Fact]
    public async Task Dispatch_HandlerImplementingSeveralInterfaces_ReceivesEachEventType()
    {
        var handler = new RecordingHandler();
        var bus = new EventBus([handler]);

        await bus.Dispatch("tenant", "id", new ItemAdded("apple"));
        await bus.Dispatch("tenant", "id", new ItemRemoved("apple"));

        Assert.Equal(
            [("tenant", "id", new ItemAdded("apple")), ("tenant", "id", new ItemRemoved("apple"))],
            handler.Received);
    }

    [Fact]
    public async Task Dispatch_SeveralHandlersOfTheSameEventType_AllReceiveIt()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        var bus = new EventBus([first, second]);

        await bus.Dispatch("tenant", "id", new ItemAdded("apple"));

        Assert.Single(first.Received);
        Assert.Single(second.Received);
    }

    [Fact]
    public async Task Dispatch_BaseTypeHandler_IsNotCalled()
    {
        var handler = new BaseTypeHandler();
        var bus = new EventBus([handler]);

        await bus.Dispatch("tenant", "id", new ItemAdded("apple"));

        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Dispatch_EventWithoutHandler_DoesNothing()
    {
        var handler = new RecordingHandler();
        var bus = new EventBus([handler]);

        await bus.Dispatch("tenant", "id", new OrderPlaced(12.5m));

        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task Dispatch_HandlerThrows_ExceptionPropagates()
    {
        var bus = new EventBus([new ThrowingHandler()]);

        await Assert.ThrowsAsync<InvalidOperationException>(() => bus.Dispatch("tenant", "id", new OrderPlaced(1m)));
    }
}
