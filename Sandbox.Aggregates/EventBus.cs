using System.Reflection;
using Sandbox.Common;
using Sandbox.Common.Readmodels;

namespace Sandbox.Aggregates;

/// <summary>
/// Dispatches events to every <see cref="IEventHandler{TEvent}"/> registered for the event's exact runtime type.
/// Handlers run synchronously, in registration order; their exceptions propagate to the caller.
/// </summary>
public class EventBus
{
    private static readonly MethodInfo InvokeMethod =
        typeof(EventBus).GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Dictionary<Type, List<Action<string, string, IEvent>>> _handlers = new();

    public EventBus(IEnumerable<IEventHandler> handlers)
    {
        foreach (var handler in handlers)
        {
            var eventTypes = handler.GetType()
                .GetInterfaces()
                .Where(itf => itf.IsGenericType && itf.GetGenericTypeDefinition() == typeof(IEventHandler<>))
                .Select(itf => itf.GetGenericArguments()[0]);

            foreach (var eventType in eventTypes)
            {
                var invoker = InvokeMethod
                    .MakeGenericMethod(eventType)
                    .CreateDelegate<Action<IEventHandler, string, string, IEvent>>();

                if (!_handlers.TryGetValue(eventType, out var list))
                {
                    list = [];
                    _handlers[eventType] = list;
                }

                list.Add((tenant, aggregateId, @event) => invoker(handler, tenant, aggregateId, @event));
            }
        }
    }

    public void Dispatch(string tenant, string aggregateId, IEvent @event)
    {
        if (!_handlers.TryGetValue(@event.GetType(), out var list)) return;
        foreach (var handle in list) handle(tenant, aggregateId, @event);
    }

    private static void Invoke<TEvent>(IEventHandler handler, string tenant, string aggregateId, IEvent @event)
        where TEvent : IEvent
        => ((IEventHandler<TEvent>)handler).Handle(tenant, aggregateId, (TEvent)@event);
}
