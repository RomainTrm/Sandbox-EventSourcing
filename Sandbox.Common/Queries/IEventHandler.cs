namespace Sandbox.Common.Queries;

public interface IEventHandler;

public interface IEventHandler<in TEvent> : IEventHandler where TEvent : IEvent
{
    Task Handle(string tenant, string aggregateId, TEvent @event);
}