---
paths:
  - "Sandbox.Aggregates/**"
  - "Sandbox.Aggregates.Tests/**"
---

# Sandbox.Aggregates: classical aggregates strategy

## Implementation style

- **Each aggregate owns its own event stream.** Aggregates never share events, and an aggregate never reads another aggregate's stream.
- To handle a command: load **all** events of the aggregate (`EventStore.LoadEvents`), rebuild its state from them, decide which new events to emit, then append them with `EventStore.SaveEvents`.
- A command that involves several aggregates is orchestrated by its **command handler**. The handler loads each aggregate, calls each one, and saves every `SaveRequest` in a single `SaveEvents` call, which keeps the save atomic across those aggregates. The handler doesn't open or commit a transaction, because `SaveEvents` does.
- Read models are updated in memory from the events that were saved.

## Event store (`EventStore.cs`)

- The table is `aggregates_events`, keyed by `(database, aggregate_name, aggregate_id, sequence_number)`. The `database` column holds `Query.TenantId`.
- **Optimistic concurrency**:
  - `SaveRequest.AggregateVersion` is the number of events already loaded.
  - New events are numbered `AggregateVersion + 1 + i`.
  - The unique index rejects a stale version, so `SaveEvents` throws. Don't add other locking.
- **Transaction**: `SaveEvents` opens its own transaction (`connection.BeginTransaction()`), inserts every event of every request, and commits. If any insert fails, disposing the transaction rolls everything back.
- **Event dispatch**: only **after the commit** does `SaveEvents` pass each saved event to the injected `EventBus` (from `Sandbox.Common`), in request order and then event order. The bus calls every `IEventHandler<T>` whose `T` is the event's **exact runtime type**, so handlers registered on a base type are not called. Handler exceptions propagate, but by then the events are already persisted.

## Event types

- Each aggregate's events are `sealed record`s that implement `IEvent`, grouped in a C# `union` that also implements `IEvent` and is marked `[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]`. For example, `union OrderEvent(BuyingOrderCreated) : IEvent` in `Aggregates/Order.cs`.
  - A union serializes as its case, with no discriminator. The structural classifier picks the case from the JSON property names, so **two cases of a union must not have the same shape**, or deserialization throws.
  - `SaveRequest` unwraps a union (`IUnion.Value`) before dispatching, so handlers subscribe to the case type (`IEventHandler<ItemBought>`).
  - `EventStore` also still supports the older `closed record` + `[JsonPolymorphic]` hierarchy, which `EventStoreTests.cs` uses.
- An aggregate keeps all of its state in a nested `sealed record State`, with `Initial(id)` and `Apply(evt)`, which switches on the union. `Rehydrate` folds the loaded events over `State.Initial`, and the decision methods read `_state`.
- Events are stored as JSONB. Keep `AllowOutOfOrderMetadataProperties = true` in the serializer options, because JSONB reorders keys and the type discriminator may not come first.
