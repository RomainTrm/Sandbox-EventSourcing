using System.Runtime.CompilerServices;
using System.Text.Json;
using Sandbox.Common;
using Sandbox.Common.Database;

namespace Sandbox.Aggregates;

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

    public record Query(string TenantId, string AggregateName, string AggregateId);
    public abstract record SaveRequest(Query Query, int AggregateVersion)
    {
        internal abstract IEnumerable<string> SerializeNewEvents(JsonSerializerOptions options);
        internal abstract IEnumerable<IEvent> GetNewEvents();
    }

    public record SaveRequest<TEvents>(Query Query, int AggregateVersion, List<TEvents> NewEvents)
        : SaveRequest(Query, AggregateVersion)
        where TEvents : IEvent
    {
        internal override IEnumerable<string> SerializeNewEvents(JsonSerializerOptions options) =>
            NewEvents.Select(evt => JsonSerializer.Serialize(evt, options));

        // The EventBus dispatches on the exact runtime type, so a union event is unwrapped to its case.
        internal override IEnumerable<IEvent> GetNewEvents() =>
            NewEvents.Select(evt => (object)evt is IUnion { Value: IEvent value } ? value : evt);
    }

    public async Task<List<TEvents>> LoadEvents<TEvents>(Query query)
    {
        var serializedEvents = await connection
            .QueryAll<string>("""
                    SELECT data 
                    FROM aggregates_events
                    WHERE database = @database AND aggregate_name = @aggregateName AND aggregate_id = @aggregateId
                    ORDER BY sequence_number
                """,
                new Dictionary<string, object>
                {
                    { "database", query.TenantId },
                    { "aggregateName", query.AggregateName },
                    { "aggregateId", query.AggregateId },
                });

        return serializedEvents
            .Select(evt => JsonSerializer.Deserialize<TEvents>(evt, JsonSerializerOptions)!)
            .ToList();
    }

    public async Task SaveEvents(IEnumerable<SaveRequest> eventsToSave)
    {
        await using var transaction = await connection.BeginTransaction();
        
        var saveRequests = eventsToSave.ToList();
        var args = saveRequests
            .SelectMany(saveRequest => saveRequest
                .SerializeNewEvents(JsonSerializerOptions)
                .Select((data, index) => new Dictionary<string, object>
                {
                    { "database", saveRequest.Query.TenantId },
                    { "aggregateName", saveRequest.Query.AggregateName },
                    { "aggregateId", saveRequest.Query.AggregateId },
                    { "sequenceNumber", saveRequest.AggregateVersion + 1 + index },
                    { "data", data },
                })
            )
            .ToList();

        await connection.ExecuteAll("""
                INSERT INTO aggregates_events (database, aggregate_name, aggregate_id, sequence_number, data) 
                VALUES (@database, @aggregateName, @aggregateId, @sequenceNumber, @data::jsonb)
            """,
            args);

        await transaction.Commit();

        // Dispatch only once every insert succeeded: a stale version throws above and reaches no handler.
        foreach (var saveRequest in saveRequests)
        foreach (var evt in saveRequest.GetNewEvents())
            await eventBus.Dispatch(saveRequest.Query.TenantId, saveRequest.Query.AggregateId, evt);
    }
}