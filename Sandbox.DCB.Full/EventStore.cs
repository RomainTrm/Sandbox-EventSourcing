using System.Text.Json;
using Sandbox.Common;
using Sandbox.Common.Database;

namespace Sandbox.DCB.Full;

/// <summary>Thrown when an event matching the query was appended after the pointer the caller loaded.</summary>
public class AppendConditionFailedException(int expectedPointer, int actualPointer)
    : Exception($"Append condition failed: expected pointer {expectedPointer}, but the query now points to {actualPointer}.")
{
    public int ExpectedPointer { get; } = expectedPointer;
    public int ActualPointer { get; } = actualPointer;
}

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

    /// <summary>Matches events whose type is one of <see cref="Types"/> (any type when empty) and that carry any of <see cref="Tags"/>.</summary>
    public record Query(List<Tag> Tags, List<Type> Types);

    /// <summary>The matching events ordered by id, and the id of the last one (0 when none match).</summary>
    public record LoadResult<TEvent>(List<TEvent> Events, int Pointer);

    /// <summary>A new event and every tag it belongs to.</summary>
    public record EventToSave<TEvent>(TEvent Event, List<Tag> Tags) where TEvent : IEvent;

    private sealed record Row(int Id, string Data);

    /// <summary>Filter on <c>dcb_events_full e</c> shared by the load and the append-condition check.</summary>
    private const string MatchesQuery = """
        EXISTS (
            SELECT 1 FROM dcb_flags_full f
            WHERE f.event_id = e.id
              AND f.database = @database
              AND (f.aggregate_name, f.aggregate_id) IN (SELECT * FROM unnest(@names, @ids))
        )
        AND (cardinality(@types) = 0 OR e.event_type = ANY(@types))
        """;

    private static Dictionary<string, object> QueryParameters(string tenantId, Query query)
    {
        if (query.Tags.Count == 0)
            throw new ArgumentException("A query needs at least one tag.", nameof(query));

        var tags = query.Tags.Distinct().ToList();
        return new Dictionary<string, object>
        {
            { "database", tenantId },
            // Npgsql takes arrays natively, so Dapper sends each one as a single text[] parameter.
            { "names", tags.Select(tag => tag.Name).ToArray() },
            { "ids", tags.Select(tag => tag.Id).ToArray() },
            { "types", query.Types.Select(type => type.Name).Distinct().ToArray() },
        };
    }

    /// <summary>Loads every event matching the query, and the pointer to pass back to <see cref="SaveEvents{TEvent}"/>.</summary>
    public async Task<LoadResult<TEvent>> LoadEvents<TEvent>(string tenantId, Query query)
        where TEvent : IEvent
    {
        var rows = await connection.QueryAll<Row>($"""
                SELECT e.id AS Id, e.data::text AS Data
                FROM dcb_events_full e
                WHERE {MatchesQuery}
                ORDER BY e.id
            """,
            QueryParameters(tenantId, query));

        return new LoadResult<TEvent>(
            rows.Select(row => JsonSerializer.Deserialize<TEvent>(row.Data, JsonSerializerOptions)!).ToList(),
            rows.Count == 0 ? 0 : rows[^1].Id);
    }

    /// <summary>
    /// Appends the events if no event matching <paramref name="query"/> was appended after <paramref name="pointer"/>,
    /// otherwise throws <see cref="AppendConditionFailedException"/>. Saves of a tenant are serialized by an advisory lock.
    /// </summary>
    public async Task SaveEvents<TEvent>(string tenantId, Query query, int pointer, IEnumerable<EventToSave<TEvent>> eventsToSave)
        where TEvent : IEvent
    {
        var queryParameters = QueryParameters(tenantId, query);
        var events = eventsToSave
            .Select(evt => evt with { Tags = evt.Tags.Distinct().ToList() })
            .ToList();
        if (events.Any(evt => evt.Tags.Count == 0))
            throw new ArgumentException("Every event needs at least one tag: the tenant is stored on the tags.", nameof(eventsToSave));
        if (events.Count == 0) return;

        // One row per (event, tag). Events are referenced by their 1-based position in the "data" array.
        var flags = events
            .SelectMany((evt, index) => evt.Tags.Select(tag => (EventPosition: index + 1, Tag: tag)))
            .ToList();

        await using var transaction = await connection.BeginTransaction();

        // Held until commit or rollback: the check below and the insert can't interleave with another save of the tenant.
        // Under READ COMMITTED, the check runs after the lock is granted, so it sees what the previous holder committed.
        await transaction.Execute(
            "SELECT pg_advisory_xact_lock(hashtext(@database))",
            new Dictionary<string, object> { { "database", tenantId } });

        var currentPointer = await transaction.QueryFirst<int>(
            $"SELECT COALESCE(MAX(e.id), 0) FROM dcb_events_full e WHERE {MatchesQuery}",
            queryParameters);
        if (currentPointer != pointer)
            throw new AppendConditionFailedException(pointer, currentPointer);

        // Event ids are drawn from the identity sequence up front so each flag can be joined to its event by position:
        // RETURNING doesn't guarantee row order. The CTE calls nextval (volatile), so Postgres evaluates it once.
        await transaction.Execute("""
                WITH new_events AS (
                    SELECT nextval(pg_get_serial_sequence('dcb_events_full', 'id'))::int AS id, event_type, data, position
                    FROM unnest(@eventTypes, @data) WITH ORDINALITY AS t(event_type, data, position)
                    ORDER BY position
                )
                , inserted AS (
                    INSERT INTO dcb_events_full (id, event_type, data)
                    SELECT id, event_type, data::jsonb FROM new_events
                )
                INSERT INTO dcb_flags_full (database, aggregate_name, aggregate_id, event_id)
                SELECT @database, f.name, f.id, e.id
                FROM unnest(@eventPositions, @names, @ids) AS f(event_position, name, id)
                JOIN new_events e ON e.position = f.event_position
            """,
            new Dictionary<string, object>
            {
                { "database", tenantId },
                { "eventTypes", events.Select(evt => evt.Event.GetType().Name).ToArray() },
                { "data", events.Select(evt => JsonSerializer.Serialize(evt.Event, JsonSerializerOptions)).ToArray() },
                { "eventPositions", flags.Select(flag => flag.EventPosition).ToArray() },
                { "names", flags.Select(flag => flag.Tag.Name).ToArray() },
                { "ids", flags.Select(flag => flag.Tag.Id).ToArray() },
            });

        await transaction.Commit();

        // Dispatch only once the save is committed: a failed append condition throws above and reaches no handler.
        foreach (var evt in events)
        foreach (var tag in evt.Tags)
            await eventBus.Dispatch(tenantId, tag.Id, evt.Event);
    }
}
