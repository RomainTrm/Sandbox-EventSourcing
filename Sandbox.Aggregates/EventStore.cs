using System.Text.Json;
using Sandbox.Common;
using Sandbox.Common.Database;

namespace Sandbox.Aggregates;

public class EventStore(INotTransactionConnection connection)
{
    public static async Task<EventStore> Build(IConnectionFactory connectionFactory)
    {
        var connection = await connectionFactory.CreateConnection();
        return new EventStore(connection);
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
    }

    public record SaveRequest<TEvents>(Query Query, int AggregateVersion, List<TEvents> NewEvents)
        : SaveRequest(Query, AggregateVersion)
        where TEvents : IEvent
    {
        internal override IEnumerable<string> SerializeNewEvents(JsonSerializerOptions options) =>
            NewEvents.Select(evt => JsonSerializer.Serialize(evt, options));
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
        var args = eventsToSave
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
    }
}