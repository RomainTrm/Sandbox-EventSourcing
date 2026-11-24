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

## Event types

- All events of the project share **one** `closed record` base type that implements `IEvent` and is marked `[JsonPolymorphic(InferClosedTypePolymorphism = true)]`. The events themselves are `sealed record` subtypes.
- Keep `AllowOutOfOrderMetadataProperties = true`, because JSONB reorders keys.

## Tests

- A failed insert aborts the shared `ImplicitRollbackTransactionConnectionFactory` transaction. After a test expects a database exception, it can't query again.
