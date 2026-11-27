using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Sandbox.Common;

namespace Sandbox.DCB.Full;

/// <summary>
/// The decision model of a command: what to load, the state built from it, and the events it decides.
/// <typeparamref name="TEvent"/> is a union of the events the decision reads; its cases are the query's event types.
/// </summary>
public interface IDecider<in TEvent, in TState> 
    where TEvent : IEvent
    where TState : IDeciderState<TState, TEvent>
{
    string GetTenant();

    /// <summary>The tags the decision depends on. With the cases of <typeparamref name="TEvent"/>, it is also the append condition of the save.</summary>
    IReadOnlyList<EventStore.Tag> BuildQuery();

    /// <summary>The events to append. Throws when the command is rejected.</summary>
    IEnumerable<EventStore.EventToSave<IEvent>> Decide(TState state);
}

public static class DeciderExtensions
{
    private static readonly JsonSerializerOptions Metadata = new() { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };

    /// <summary>Runs a decider: load the query, rehydrate, decide, then save under the loaded pointer.</summary>
    public static async Task Process<TEvent, TState>(this IDecider<TEvent, TState> decider, EventStore eventStore)
        where TEvent : IEvent
        where TState : IDeciderState<TState, TEvent>
    {
        var tenant = decider.GetTenant();
        var query = new EventStore.Query([..decider.BuildQuery()], UnionCases<TEvent>());

        var loaded = await eventStore.LoadEvents<TEvent>(tenant, query);
        var state = IDeciderState<TState, TEvent>.Build(loaded.Events);
        var events = decider.Decide(state);

        await eventStore.SaveEvents(tenant, query, loaded.Pointer, events);
    }

    private static List<Type> UnionCases<TEvent>() =>
        Metadata.GetTypeInfo(typeof(TEvent)).UnionCases.Select(unionCase => unionCase.CaseType).ToList()
        ?? throw new InvalidOperationException($"{typeof(TEvent)} must be a union of the events the decider reads.");
}
