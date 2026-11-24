---
paths:
  - "Sandbox.DCB.Full/**"
  - "Sandbox.DCB.Full.Tests/**"
---

# Sandbox.DCB.Full: DCB with a query and a pointer

See `adrs/DCB event store.md` (the `dcb_flags_full` option) and [dcb.events](https://dcb.events).

## Event store (`EventStore.cs`)

- **Tables**:
  - `dcb_events_full` stores each event once: `event_type` (the CLR `Type.Name`) and `data` (JSONB).
  - `dcb_flags_full` attaches an event to a tag: `(database, aggregate_name, aggregate_id, event_id)`. There is no sequence number.
  - `database` is the tenant, and `Tag(Name, Id)` maps to `aggregate_name` and `aggregate_id`. The tenant lives only on the flags, so **every event needs at least one tag**, and so does every query. Without one, the store throws `ArgumentException`.
- **Query**: `Query(Tags, Types)` matches an event whose `event_type` is one of `Types` (any type when `Types` is empty) **and** that carries **any** of `Tags`. Name and id are matched as a pair with `unnest(@names, @ids)`.
- **Load**: `LoadEvents<TEvent>(tenant, query)` returns `LoadResult(Events, Pointer)`.
  - Events are ordered by id, and each one appears once even when it carries several of the requested tags.
  - `Pointer` is the id of the last matching event, or 0 when nothing matches.
- **Append condition**: `SaveEvents(tenant, query, pointer, events)` saves only if the query's current pointer still equals `pointer`. Otherwise it throws `AppendConditionFailedException`.
  - A new event that doesn't match the query, because it is on other tags or of another type, doesn't invalidate the pointer. That is what DCB is for.
- **Locking**: inside its own transaction (`await using var transaction = await connection.BeginTransaction()`), `SaveEvents` first takes `pg_advisory_xact_lock(hashtext(tenant))`. It then checks the pointer and inserts, so saves of one tenant can't interleave. The lock is released at commit or rollback. Don't add C# locks.
- **Insert**: the events and their flags are written by **one statement**. Ids are reserved with `nextval(pg_get_serial_sequence('dcb_events_full', 'id'))` in a CTE, and each flag is joined to its event by position, because the order of `RETURNING` rows isn't guaranteed.
- **Event dispatch** happens only **after the commit**, **once per tag**: `Dispatch(tenant, tag.Id, evt)`, in event order and then tag order.

## Event types

- All events of the project share **one** `closed record` base type that implements `IEvent` and is marked `[JsonPolymorphic(InferClosedTypePolymorphism = true)]`. The events themselves are `sealed record` subtypes. Because `event_type` holds `Type.Name`, two event types must not share a name.
- Keep `AllowOutOfOrderMetadataProperties = true`, because JSONB reorders keys.

## Tests

- The test factory runs everything in one transaction, and advisory locks are re-entrant within a session. So the tests check the pointer logic one save after another; they can't race two saves.
