using System.Reflection;
using Sandbox.Common.Readmodels;

namespace Sandbox.Common;

/// <summary>
/// Dispatches events to every <see cref="IEventHandler{TEvent}"/> registered for the event's exact runtime type.
/// Handlers are awaited one after another, in registration order; their exceptions propagate to the caller.
/// </summary>
public class EventBus
{
    private static readonly MethodInfo InvokeMethod =
        typeof(EventBus).GetMethod(nameof(Invoke), BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly Dictionary<Type, List<Func<string, string, IEvent, Task>>> _handlers = new();

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
                    .CreateDelegate<Func<IEventHandler, string, string, IEvent, Task>>();

                if (!_handlers.TryGetValue(eventType, out var list))
                {
                    list = [];
                    _handlers[eventType] = list;
                }

                list.Add((tenant, aggregateId, @event) => invoker(handler, tenant, aggregateId, @event));
            }
        }
    }

    public async Task Dispatch(string tenant, string aggregateId, IEvent @event)
    {
        if (!_handlers.TryGetValue(@event.GetType(), out var list)) return;
        foreach (var handle in list) 
            await handle(tenant, aggregateId, @event);
    }

    private static Task Invoke<TEvent>(IEventHandler handler, string tenant, string aggregateId, IEvent @event)
        where TEvent : IEvent
        => ((IEventHandler<TEvent>)handler).Handle(tenant, aggregateId, (TEvent)@event);
}
