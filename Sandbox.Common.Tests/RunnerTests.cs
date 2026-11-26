using Sandbox.Common.Tests.Fakes;

namespace Sandbox.Common.Tests;

public class RunnerTests
{
    private readonly TestRunner _runner = new(new NullConnectionFactory());

    [Fact]
    public void Request_BeforeAnyCommand_ReturnsInitialState()
    {
        Assert.Equal(0, _runner.Request<GetStock, int>(new GetStock("apple")));
    }

    [Fact]
    public async Task RunCommand_DispatchedEvents_ReachTheReadModelServingQueries()
    {
        await _runner.RunCommand(new IncreaseStock("apple", 3));
        await _runner.RunCommand(new IncreaseStock("apple", 2));

        Assert.Equal(5, _runner.Request<GetStock, int>(new GetStock("apple")));
    }

    [Fact]
    public async Task RunCommand_WithoutHandler_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _runner.RunCommand(new UnhandledCommand()));
    }

    [Fact]
    public void Request_WithoutHandler_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _runner.Request<UnhandledQuery, int>(new UnhandledQuery()));
    }
}
