using Sandbox.Common;

namespace Sandbox.DCB.Lite;

/// <summary>A stream to load: its tag, and the union of the events that can be flagged on it.</summary>
public abstract record Stream(EventStore.Tag Tag)
{
    internal abstract Type EventType { get; }

    /// <summary>Loads every given tag as a stream of this stream's union.</summary>
    internal abstract Task<Dictionary<EventStore.TagQuery, List<object>>> Load(EventStore eventStore, string tenant, IEnumerable<EventStore.Tag> tags);
}

public sealed record Stream<TEvent>(EventStore.Tag Tag) : Stream(Tag) where TEvent : IEvent
{
    internal override Type EventType => typeof(TEvent);

    internal override async Task<Dictionary<EventStore.TagQuery, List<object>>> Load(EventStore eventStore, string tenant, IEnumerable<EventStore.Tag> tags)
    {
        var loaded = await eventStore.LoadEvents<TEvent>(tenant, tags);
        return loaded.ToDictionary(stream => stream.Key, stream => stream.Value.Cast<object>().ToList());
    }
}

/// <summary>The loaded streams, read back with the union type of each stream.</summary>
public sealed class LoadedStreams(IReadOnlyDictionary<EventStore.Tag, List<object>> streams)
{
    public IReadOnlyList<TEvent> Get<TEvent>(Stream<TEvent> stream) where TEvent : IEvent =>
        streams.TryGetValue(stream.Tag, out var events)
            ? events.Cast<TEvent>().ToList()
            : throw new ArgumentException($"Stream {stream.Tag} was not part of the query.", nameof(stream));
}

/// <summary>The decision model of a command: what to load, the state built from it, and the events it decides.</summary>
public interface IDecider<TState>
{
    string GetTenant();

    /// <summary>
    /// The streams the decision depends on. Their loaded versions are the expected versions of the save,
    /// so every tag used by a decided event must be listed here.
    /// </summary>
    IReadOnlyList<Stream> BuildQuery();

    /// <summary>One stream per queried tag. An event on several queried tags appears in each of their streams.</summary>
    TState Rehydrate(LoadedStreams streams);

    /// <summary>The events to append. Throws when the command is rejected.</summary>
    IEnumerable<EventStore.EventToSave<IEvent>> Decide(TState state);
}

public static class DeciderExtensions
{
    /// <summary>Runs a decider: load the streams, rehydrate, decide, then save with the loaded versions.</summary>
    public static async Task Process<TState>(this IDecider<TState> decider, EventStore eventStore)
    {
        var tenant = decider.GetTenant();

        // One load per union type, since a load deserializes every stream into the same type.
        var tagQueries = new List<EventStore.TagQuery>();
        var streams = new Dictionary<EventStore.Tag, List<object>>();
        foreach (var group in decider.BuildQuery().DistinctBy(stream => stream.Tag).GroupBy(stream => stream.EventType))
        {
            var loaded = await group.First().Load(eventStore, tenant, group.Select(stream => stream.Tag));
            foreach (var (tagQuery, events) in loaded)
            {
                tagQueries.Add(tagQuery);
                streams[tagQuery.Tag] = events;
            }
        }

        var state = decider.Rehydrate(new LoadedStreams(streams));
        var newEvents = decider.Decide(state);

        await eventStore.SaveEvents(tenant, tagQueries, newEvents);
    }
}
