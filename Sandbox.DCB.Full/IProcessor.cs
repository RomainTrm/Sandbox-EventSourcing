using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sandbox.Common;
using Sandbox.Common.Commands;

namespace Sandbox.DCB.Full;

/// <summary>
/// The decision model of a command: what to load, the state built from it, and the events it decides.
/// <typeparamref name="TEvent"/> is a union of the events the decision reads; its cases are the query's event types.
/// </summary>
public interface IProcessor<in TCommand, TEvent, TState>
    where TCommand : ICommand
    where TEvent : IEvent
{
    string GetTenant(TCommand command);

    /// <summary>The tags the decision depends on. With the cases of <typeparamref name="TEvent"/>, it is also the append condition of the save.</summary>
    IReadOnlyList<EventStore.Tag> BuildQuery(TCommand command);

    /// <summary>The state before any loaded event is applied.</summary>
    TState InitialState { get; }

    /// <summary>Folds one loaded event into the state. Events are applied in load order.</summary>
    TState ApplyEvent(TState state, TEvent evt);

    /// <summary>The events to append. Throws when the command is rejected.</summary>
    IEnumerable<EventStore.EventToSave<IEvent>> Execute(TCommand command, TState state);
}

public static class ProcessorExtensions
{
    private static readonly JsonSerializerOptions Metadata = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    /// <summary>Runs a processor: load the query, rehydrate, execute, then save under the loaded pointer.</summary>
    public static async Task Process<TCommand, TEvent, TState>(this IProcessor<TCommand, TEvent, TState> processor, EventStore eventStore, TCommand command)
        where TCommand : ICommand
        where TEvent : IEvent
    {
        var tenant = processor.GetTenant(command);
        var query = new EventStore.Query([..processor.BuildQuery(command)], UnionCases<TEvent>());

        var loaded = await eventStore.LoadEvents<TEvent>(tenant, query);
        var state = loaded.Events.Aggregate(processor.InitialState, processor.ApplyEvent);
        var events = processor.Execute(command, state);

        await eventStore.SaveEvents(tenant, query, loaded.Pointer, events);
    }

    private static List<Type> UnionCases<TEvent>() =>
        Metadata.GetTypeInfo(typeof(TEvent)).UnionCases.Select(unionCase => unionCase.CaseType).ToList()
        ?? throw new InvalidOperationException($"{typeof(TEvent)} must be a union of the events the processor reads.");
}
