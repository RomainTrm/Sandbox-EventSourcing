using Sandbox.Common.Commands;
using Sandbox.Common.Queries;

namespace Sandbox.Common;

public interface IRunner
{
    Task RunCommand<TCommand>(TCommand command) where TCommand : ICommand;
    TResult Request<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>;
}