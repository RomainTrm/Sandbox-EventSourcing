using Sandbox.Common;
using Sandbox.Common.Commands;
using Sandbox.Common.Database;
using Sandbox.Common.Queries;

namespace Sandbox.Aggregates;

public class Runner(IConnectionFactory  connectionFactory) : IRunner
{
    public async Task RunCommand<TCommand>(TCommand command) where TCommand : ICommand
    {
        var commandHandler = BuildCommandHandler<TCommand>();
        await commandHandler.Handle(command);
    }
    
    private ICommandHandler<TCommand> BuildCommandHandler<TCommand>() where TCommand : ICommand
    {
        throw new NotImplementedException();
    }

    public TResult Request<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>
    {
        var queryHandler = BuildQueryHandler<TQuery, TResult>();
        return queryHandler.Request(query);
    }

    private IQueryHandler<TQuery, TResult> BuildQueryHandler<TQuery, TResult>() where TQuery : IQuery<TResult>
    {
        throw new NotImplementedException();
    }
}