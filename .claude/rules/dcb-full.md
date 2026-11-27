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

## Command processing (`IDecider.cs`, `IDeciderState.cs`)

- A command's decision lives in a decider in `Commands/` (e.g. `CreateBuyingOrderDecider`). It implements `IDecider<TEvent, TState>`. Its only dependency is the command, taken by its primary constructor (`CreateBuyingOrderDecider(CreateBuyingOrder command)`), so none of its methods take the command:
  - `TEvent` is a nested `union Event(...)` of the events the decision **reads**.
  - `GetTenant()`
  - `BuildQuery()` returns the tags the decision depends on. `Process` builds the `Query` from those tags and the union's cases (`JsonTypeInfo.UnionCases`), so the type filter is never written by hand.
  - `Decide(state)` returns `EventToSave<IEvent>`s, the plain event records. They don't have to be cases of the union. It throws to reject the command.
- `TState` is a nested `State` record that implements `IDeciderState<State, Event>`. That interface is the only base type it declares:
  - `public static State InitialState` is the state before any loaded event is applied. It is `static abstract` in the interface, so a state without one doesn't compile. Don't make it `static virtual`: a default-implemented static auto-property is silently `null`.
  - `ApplyEvent(evt)` is an instance method that returns the next state (`this with { ... }`). Events are applied in load order. The nested state doesn't see the decider's command, so anything it depends on must already be selected by the query's tags.
  - `Process` rehydrates with `IDeciderState<TState, TEvent>.Build(loaded.Events)`, which folds from `InitialState`. Don't write the fold by hand.
- **Deciders don't implement `ICommandHandler<>`.** A separate command handler (e.g. `CreateBuyingOrderHandler`, constructor `(IConnectionFactory, EventBus)` so that `Runner` discovers it) doesn't hold state. Its `Handle` builds the `EventStore` (`EventStore.Build(connectionFactory, eventBus)`) and a new decider from the command (`new CreateBuyingOrderDecider(command)`), then calls `decider.Process(eventStore)`.
- A decider never calls the `EventStore`. `Process` loads the query, rehydrates, decides, and saves with the **same query and the loaded pointer** as the append condition.

## Event types

- Events are plain `sealed record`s implementing `IEvent` (`Events.cs`). They have no shared base type. Because `event_type` holds `Type.Name`, two event types must not share a name.
- **One union per decider**: it lists only the events that decider requests, and it is marked `[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]`. The query's type filter guarantees that only its cases are loaded.
  - A union is read without a discriminator: the structural classifier picks the case from the JSON property names. So **two cases of a union must not have the same shape**, or deserialization throws.
- On save, the `EventStore` serializes an `IEvent`-typed event as its runtime type, because `IEvent` itself would serialize as `{}`. It unwraps a union (`IUnion.Value`) for `event_type` and dispatch. A `closed record` hierarchy (the test fakes) is still serialized through its declared type and keeps its discriminator.
- Keep `AllowOutOfOrderMetadataProperties = true`, because JSONB reorders keys.

## Tests

- The test factory runs everything in one transaction, and advisory locks are re-entrant within a session. So the tests check the pointer logic one save after another; they can't race two saves.
