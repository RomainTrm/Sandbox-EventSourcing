using Sandbox.Aggregates.Commands;
using Sandbox.Common;
using Sandbox.Common.Commands;
using Sandbox.Common.Database;
using Sandbox.Common.Queries;

namespace Sandbox.Aggregates;

public class Runner(IConnectionFactory connectionFactory) : IRunner
{
    // In-memory read model handlers get registered here.
    private readonly EventBus _eventBus = new([]);

    public async Task RunCommand<TCommand>(TCommand command) where TCommand : ICommand
    {
        var commandHandler = BuildCommandHandler<TCommand>();
        await commandHandler.Handle(command);
    }
    
    private ICommandHandler<TCommand> BuildCommandHandler<TCommand>() where TCommand : ICommand
    {
        if (typeof(TCommand) == typeof(CreateBuyingOrder)) return (ICommandHandler<TCommand>) new CreateBuyingOrderHandler(connectionFactory, _eventBus);
        throw new NotImplementedException();
    }

    public TResult Request<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>
    {
        var queryHandler = BuildQueryHandler<TQuery, TResult>();
        return queryHandler.Request(query);
    }

    private static IQueryHandler<TQuery, TResult> BuildQueryHandler<TQuery, TResult>() where TQuery : IQuery<TResult>
    {
        throw new NotImplementedException();
    }
}