using System.Reflection;
using Sandbox.Common.Commands;
using Sandbox.Common.Database;
using Sandbox.Common.Queries;

namespace Sandbox.Common;

/// <summary>
/// Discovers every concrete <see cref="IEventHandler"/>, <see cref="ICommandHandler{TCommand}"/> and
/// <see cref="IQueryHandler{TQuery,TResult}"/> in the assembly of the concrete subclass, and instantiates each
/// class once (a class implementing several of these interfaces shares one instance).
/// Event handlers are instantiated first, since the <see cref="EventBus"/> is built from them: their constructors
/// may only take an <see cref="IConnectionFactory"/>. Other handlers may also take the <see cref="EventBus"/>.
/// </summary>
public abstract class Runner
{
    private readonly Dictionary<Type, object> _handlers = new();

    protected Runner(IConnectionFactory connectionFactory)
    {
        var candidates = GetType().Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false, ContainsGenericParameters: false })
            .Where(type => typeof(IEventHandler).IsAssignableFrom(type) || HandlerInterfaces(type).Any())
            .ToList();

        var eventHandlerServices = new Dictionary<Type, object> { [typeof(IConnectionFactory)] = connectionFactory };
        var instances = candidates
            .Where(type => typeof(IEventHandler).IsAssignableFrom(type))
            .Select(type => Instantiate(type, eventHandlerServices))
            .ToList();

        var eventBus = new EventBus(instances.Cast<IEventHandler>());

        var services = new Dictionary<Type, object>(eventHandlerServices) { [typeof(EventBus)] = eventBus };
        instances.AddRange(candidates
            .Where(type => !typeof(IEventHandler).IsAssignableFrom(type))
            .Select(type => Instantiate(type, services)));

        foreach (var instance in instances)
        foreach (var handlerInterface in HandlerInterfaces(instance.GetType()))
        {
            if (!_handlers.TryAdd(handlerInterface, instance))
                throw new InvalidOperationException($"Both {_handlers[handlerInterface].GetType()} and {instance.GetType()} implement {handlerInterface}.");
        }
    }

    public async Task RunCommand<TCommand>(TCommand command) where TCommand : ICommand
    {
        var commandHandler = BuildCommandHandler<TCommand>();
        await commandHandler.Handle(command);
    }

    private ICommandHandler<TCommand> BuildCommandHandler<TCommand>() where TCommand : ICommand
        => _handlers.TryGetValue(typeof(ICommandHandler<TCommand>), out var handler)
            ? (ICommandHandler<TCommand>)handler
            : throw new InvalidOperationException($"No handler registered for command {typeof(TCommand)}.");

    public TResult Request<TQuery, TResult>(TQuery query) where TQuery : IQuery<TResult>
    {
        var queryHandler = BuildQueryHandler<TQuery, TResult>();
        return queryHandler.Request(query);
    }

    private IQueryHandler<TQuery, TResult> BuildQueryHandler<TQuery, TResult>() where TQuery : IQuery<TResult>
        => _handlers.TryGetValue(typeof(IQueryHandler<TQuery, TResult>), out var handler)
            ? (IQueryHandler<TQuery, TResult>)handler
            : throw new InvalidOperationException($"No handler registered for query {typeof(TQuery)}.");

    private static IEnumerable<Type> HandlerInterfaces(Type type)
        => type.GetInterfaces()
            .Where(itf => itf.IsGenericType && (itf.GetGenericTypeDefinition() == typeof(ICommandHandler<>) || itf.GetGenericTypeDefinition() == typeof(IQueryHandler<,>)));

    private static object Instantiate(Type type, IReadOnlyDictionary<Type, object> services)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
        if (constructors.Length != 1) throw new InvalidOperationException($"{type} must have exactly one public constructor.");

        var arguments = constructors[0].GetParameters()
            .Select(parameter => services.TryGetValue(parameter.ParameterType, out var service)
                ? service
                : throw new InvalidOperationException($"Cannot resolve parameter '{parameter.Name}' of type {parameter.ParameterType} for {type}.")
            )
            .ToArray();

        return constructors[0].Invoke(arguments);
    }
}
