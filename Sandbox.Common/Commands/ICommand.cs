namespace Sandbox.Common.Commands;

public interface ICommand;

public interface ICommandHandler<in TCommand> where TCommand : ICommand
{
    Task Handle(TCommand command);
}