using System.Text.Json;
using Sandbox.Common;
using Sandbox.Common.Database;

namespace Sandbox.DCB.Lite;

public class EventStore(INotTransactionConnection connection, EventBus eventBus)
{
    public static async Task<EventStore> Build(IConnectionFactory connectionFactory, EventBus eventBus)
    {
        var connection = await connectionFactory.CreateConnection();
        return new EventStore(connection, eventBus);
    }

    private static readonly JsonSerializerOptions JsonSerializerOptions = new()
    {
        InferClosedTypePolymorphism = true,
        // JSONB doesn't preserve key order, so the type discriminator may not come first.
        AllowOutOfOrderMetadataProperties = true,
    };

    /// <summary>Identifies an event stream inside a tenant, e.g. ("item", "42").</summary>
    public record Tag(string Name, string Id);

    /// <summary>A tag and the number of events already in its stream.</summary>
    public record TagQuery(Tag Tag, int Version);

    /// <summary>A new event and every tag (stream) it belongs to.</summary>
    public record EventToSave<TEvent>(TEvent Event, List<Tag> Tags) where TEvent : IEvent;

    private sealed record Row(string AggregateName, string AggregateId, int SequenceNumber, string Data);

    /// <summary>
    /// Loads the stream of each tag. Every requested tag is present in the result, with version 0 and no events
    /// when its stream is empty. An event attached to several requested tags appears in each of their streams.
    /// </summary>
    public async Task<Dictionary<TagQuery, List<TEvent>>> LoadEvents<TEvent>(string tenantId, IEnumerable<Tag> tags)
        where TEvent : IEvent
    {
        var requestedTags = tags.Distinct().ToList();
        if (requestedTags.Count == 0) return [];

        var parameters = new Dictionary<string, object>
        {
            { "database", tenantId },
            // Npgsql takes arrays natively, so Dapper sends each one as a single text[] parameter.
            { "names", requestedTags.Select(t => t.Name).ToArray() },
            { "ids", requestedTags.Select(t => t.Id).ToArray() },
        };

        var rows = await connection.QueryAll<Row>("""
                SELECT f.aggregate_name AS AggregateName, 
                       f.aggregate_id AS AggregateId,
                       f.sequence_number AS SequenceNumber, 
                       e.data::text AS Data
                FROM dcb_flags_lite f
                JOIN dcb_events_lite e ON e.id = f.event_id
                WHERE f.database = @database 
                  AND (f.aggregate_name, f.aggregate_id) IN (SELECT * FROM unnest(@names, @ids))
                ORDER BY f.sequence_number
            """,
            parameters);

        var rowsByTag = rows.ToLookup(row => new Tag(row.AggregateName, row.AggregateId));
        return requestedTags.ToDictionary(
            tag => new TagQuery(tag, rowsByTag[tag].Count()),
            tag => rowsByTag[tag]
                .Select(row => JsonSerializer.Deserialize<TEvent>(row.Data, JsonSerializerOptions)!)
                .ToList());
    }

    /// <summary>
    /// Appends the events and flags each of them on its tags. For each tag, new flags are numbered
    /// <c>Version + 1 + i</c> from the matching <see cref="TagQuery"/>; a stale version hits the unique index and throws.
    /// Every tag used by an event must have a <see cref="TagQuery"/> (version 0 for a new stream).
    /// </summary>
    public async Task SaveEvents<TEvent>(string tenantId, List<TagQuery> tagQueries, IEnumerable<EventToSave<TEvent>> eventsToSave)
        where TEvent : IEvent
    {
        var events = eventsToSave
            .Select(evt => evt with { Tags = evt.Tags.Distinct().ToList() })
            .ToList();
        if (events.Count == 0) return;

        var nextSequenceNumbers = tagQueries.ToDictionary(query => query.Tag, query => query.Version + 1);
        var unknownTags = events.SelectMany(evt => evt.Tags).Distinct().Where(tag => !nextSequenceNumbers.ContainsKey(tag)).ToList();
        if (unknownTags.Count > 0)
            throw new ArgumentException(
                $"No expected version for tags: {string.Join(", ", unknownTags)}. Pass a TagQuery for each of them.",
                nameof(tagQueries));

        // One row per (event, tag). Events are referenced by their 1-based position in the "data" array.
        var flags = events
            .SelectMany((evt, index) => evt.Tags.Select(tag => (EventPosition: index + 1, Tag: tag)))
            .Select(flag => (flag.EventPosition, flag.Tag, SequenceNumber: nextSequenceNumbers[flag.Tag]++))
            .ToList();

        await using var transaction = await connection.BeginTransaction();

        // Event ids are drawn from the identity sequence up front so each flag can be joined to its event by position:
        // RETURNING doesn't guarantee row order. The CTE calls nextval (volatile), so Postgres evaluates it once.
        await transaction.Execute("""
                WITH new_events AS (
                    SELECT nextval(pg_get_serial_sequence('dcb_events_lite', 'id'))::int AS id, data, position
                    FROM unnest(@data) WITH ORDINALITY AS t(data, position)
                    ORDER BY position
                )
                , inserted AS (
                    INSERT INTO dcb_events_lite (id, data)
                    SELECT id, data::jsonb FROM new_events
                )
                INSERT INTO dcb_flags_lite (database, aggregate_name, aggregate_id, sequence_number, event_id)
                SELECT @database, f.name, f.id, f.sequence_number, e.id
                FROM unnest(@eventPositions, @names, @ids, @sequenceNumbers) AS f(event_position, name, id, sequence_number)
                JOIN new_events e ON e.position = f.event_position
            """,
            new Dictionary<string, object>
            {
                { "database", tenantId },
                { "data", events.Select(evt => JsonSerializer.Serialize(evt.Event, JsonSerializerOptions)).ToArray() },
                { "eventPositions", flags.Select(flag => flag.EventPosition).ToArray() },
                { "names", flags.Select(flag => flag.Tag.Name).ToArray() },
                { "ids", flags.Select(flag => flag.Tag.Id).ToArray() },
                { "sequenceNumbers", flags.Select(flag => flag.SequenceNumber).ToArray() },
            });

        await transaction.Commit();

        // Dispatch only once every insert succeeded: a stale version throws above and reaches no handler.
        foreach (var evt in events)
        foreach (var tag in evt.Tags)
            eventBus.Dispatch(tenantId, tag.Id, evt.Event);
    }
}
