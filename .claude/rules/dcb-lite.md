---
paths:
  - "Sandbox.DCB.Lite/**"
  - "Sandbox.DCB.Lite.Tests/**"
---

# Sandbox.DCB.Lite: DCB with a sequence number per tag

See `adrs/DCB event store.md` for why this variant exists (the `dcb_flags_lite` option).

## Event store (`EventStore.cs`)

- **Tables**:
  - `dcb_events` holds each event once, as JSONB.
  - `dcb_flags_lite` attaches an event to a tag: `(database, aggregate_name, aggregate_id, sequence_number, event_id)`, with a unique index on `(database, aggregate_name, aggregate_id, sequence_number)`.
  - `database` is the tenant, and `Tag(Name, Id)` maps to `aggregate_name` and `aggregate_id`.
- **An event can belong to several tags.** It is inserted once in `dcb_events` and gets one flag per tag. If an event lists the same tag twice, it is flagged once.
- **Load**: `LoadEvents<TEvent>(tenant, tags)` returns `Dictionary<TagQuery, List<TEvent>>`.
  - Each stream is ordered by `sequence_number`.
  - Every requested tag is a key. `TagQuery.Version` is the number of events in that tag's stream, and an unknown tag gets version 0 with an empty list.
- **Optimistic concurrency**:
  - `SaveEvents(tenant, tagQueries, eventsToSave)` numbers each tag's new flags `Version + 1 + i`, following event order.
  - The unique index rejects a stale version, so `SaveEvents` throws. Don't add other locking.
  - Every tag that an event uses needs a `TagQuery` (version 0 for a new stream). Otherwise `SaveEvents` throws `ArgumentException` before it touches the database.
- **Transaction**: `SaveEvents` opens its own transaction (`await using var transaction = await connection.BeginTransaction()`), like `Sandbox.Aggregates`, runs the save and commits.
- **Single statement**: inside that transaction, the events and their flags are written by **one SQL statement**, with the values passed as arrays and `unnest`ed.
  - Event ids come from `nextval(pg_get_serial_sequence('dcb_events', 'id'))` in a CTE, so each flag joins to its event by its position in the array. Don't rely on the order of `RETURNING` rows, because Postgres doesn't guarantee it.
- **Event dispatch** happens only **after the commit**, **once per tag**: `Dispatch(tenant, tag.Id, evt)`, in event order and then tag order. The exact-runtime-type rule of `EventBus` applies.

## Command processing (`IProcessor.cs`)

- A command's decision lives in a processor in `Commands/` (e.g. `CreateBuyingOrderProcessor`). It implements `IProcessor<TCommand, TState>` and has no dependencies:
  - `GetTenant`
  - `BuildQuery` returns the typed `Stream<TUnion>`s to load, built with the `Streams` helpers (`Streams.Order(id)`, `Streams.Item(id)`).
  - `Rehydrate` builds a `TState` (a nested record) from `LoadedStreams`. `streams.Get(Streams.Order(id))` returns that stream's events as its union, so a `switch` over them is exhaustive.
  - `Execute` returns `EventToSave<IEvent>`s: the plain event records, tagged with `Streams.X(id).Tag`. It throws to reject the command.
- `Process` loads the streams with one `LoadEvents<TUnion>` per union type, because a load deserializes every stream into the same type.
- **Processors don't implement `ICommandHandler<>`.** A separate command handler (e.g. `CreateBuyingOrderHandler`, constructor `(IConnectionFactory, EventBus)` so that `Runner` discovers it) doesn't hold state. Its `Handle` builds the `EventStore` (`EventStore.Build(connectionFactory, eventBus)`) and a new processor, then calls `processor.Process(eventStore, command)`.
- A processor never calls the `EventStore`. `Process` loads, rehydrates, executes, and saves with the loaded `TagQuery`s. So **every tag used by an executed event must be in `BuildQuery`**.

## Event types

- Events are plain `sealed record`s implementing `IEvent` (`Events.cs`). They have no shared base type.
- **One union per stream**: each kind of stream has a `union` of every event that can be flagged on it, for example `union OrderEvent(BuyingOrderCreated, ItemBought) : IEvent` and `union ItemEvent(ItemBought) : IEvent`. Each union is marked `[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]`. When an event starts being flagged on a stream, add it to that stream's union.
  - A union is read without a discriminator: the structural classifier picks the case from the JSON property names. So **two cases of a union must not have the same shape**, or deserialization throws.
- On save, the `EventStore` serializes an `IEvent`-typed event as its runtime type, because `IEvent` itself would serialize as `{}`. It unwraps a union (`IUnion.Value`) before dispatching. A `closed record` hierarchy (the test fakes) is still serialized through its declared type and keeps its discriminator.
- Keep `AllowOutOfOrderMetadataProperties = true`, because JSONB reorders keys.

## Tests

- A failed insert aborts the shared `ImplicitRollbackTransactionConnectionFactory` transaction. After a test expects a database exception, it can't query again.
