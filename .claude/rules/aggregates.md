---
paths:
  - "Sandbox.Aggregates/**"
  - "Sandbox.Aggregates.Tests/**"
---

# Sandbox.Aggregates: classical aggregates strategy

## Implementation style

- **Each aggregate owns its own event stream.** Aggregates never share events, and an aggregate never reads another aggregate's stream.
- To handle a command: load **all** events of the aggregate (`EventStore.LoadEvents`), rebuild its state from them, decide which new events to emit, then append them with `EventStore.SaveEvents`.
- A command that involves several aggregates is orchestrated by its **command handler**. The handler loads each aggregate, calls each one, and saves every `SaveRequest` in a single `SaveEvents` call inside one transaction. The handler owns the transaction and its `Commit()`.
- Read models are updated in memory from the events that were saved.

## Event store (`EventStore.cs`)

- The table is `aggregates_events`, keyed by `(database, aggregate_name, aggregate_id, sequence_number)`. The `database` column holds `Query.TenantId`.
- **Optimistic concurrency**:
  - `SaveRequest.AggregateVersion` is the number of events already loaded.
  - New events are numbered `AggregateVersion + 1 + i`.
  - The unique index rejects a stale version, so `SaveEvents` throws. Don't add other locking.

## Event types

- Each aggregate defines one `closed record` base type that implements `IEvent` and is marked `[JsonPolymorphic(InferClosedTypePolymorphism = true)]`. Its events are `sealed record` subtypes. `EventStoreTests.cs` shows the pattern.
- Events are stored as JSONB. Keep `AllowOutOfOrderMetadataProperties = true` in the serializer options, because JSONB reorders keys and the type discriminator may not come first.
