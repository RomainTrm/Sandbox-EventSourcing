using Sandbox.Common;
using Sandbox.Common.Database;
using Sandbox.DCB.Full.Tests.Fakes;
using static Sandbox.DCB.Full.EventStore;

namespace Sandbox.DCB.Full.Tests;

public class EventStoreTests : IAsyncLifetime
{
    private const string TagName = "item";

    private ImplicitRollbackTransactionConnectionFactory _factory = null!;
    private INotTransactionConnection _connection = null!;
    private EventStore _store = null!;
    private readonly string _tenant = NewTenant();

    public async Task InitializeAsync()
    {
        _factory = new ImplicitRollbackTransactionConnectionFactory(new DatabaseConfig());
        _connection = await _factory.CreateConnection();
        _store = new EventStore(_connection, new EventBus([]));
    }

    public async Task DisposeAsync()
    {
        await _connection.DisposeAsync();
        await ((IAsyncDisposable)_factory).DisposeAsync();
    }

    private static string NewTenant() => $"tenant-{Guid.NewGuid()}";
    private static Tag NewTag(string name = TagName, string? id = null) => new(name, id ?? Guid.NewGuid().ToString());
    private static Query AnyType(params Tag[] tags) => new([..tags], []);
    private static Query OfTypes(Tag[] tags, params Type[] types) => new([..tags], [..types]);
    private static EventToSave<TestEvent> On(TestEvent evt, params Tag[] tags) => new(evt, [..tags]);

    private Task<LoadResult<TestEvent>> Load(Query query, string? tenant = null) =>
        _store.LoadEvents<TestEvent>(tenant ?? _tenant, query);

    /// <summary>Saves with the pointer freshly loaded for the query.</summary>
    private async Task Append(Query query, params EventToSave<TestEvent>[] events) =>
        await _store.SaveEvents(_tenant, query, (await Load(query)).Pointer, events);

    // --- Load ---

    [Fact]
    public async Task LoadEvents_NothingMatches_ReturnsNoEventsAndPointer0()
    {
        var result = await Load(AnyType(NewTag()));

        Assert.Empty(result.Events);
        Assert.Equal(0, result.Pointer);
    }

    [Fact]
    public async Task LoadEvents_ReturnsEventsInSavedOrder_AndPointerAfterPreviousOne()
    {
        var tag = NewTag();
        await Append(AnyType(tag), On(new ItemAdded("apple"), tag));
        var first = await Load(AnyType(tag));

        await Append(AnyType(tag), On(new ItemAdded("pear"), tag), On(new ItemRemoved("apple"), tag));
        var result = await Load(AnyType(tag));

        Assert.Equal([new ItemAdded("apple"), new ItemAdded("pear"), new ItemRemoved("apple")], result.Events);
        Assert.True(first.Pointer > 0);
        Assert.True(result.Pointer > first.Pointer);
    }

    [Fact]
    public async Task LoadEvents_FiltersByType()
    {
        var tag = NewTag();
        await Append(AnyType(tag), On(new ItemAdded("apple"), tag), On(new ItemRemoved("apple"), tag), On(new ItemAdded("pear"), tag));

        var result = await Load(OfTypes([tag], typeof(ItemAdded)));

        Assert.Equal([new ItemAdded("apple"), new ItemAdded("pear")], result.Events);
    }

    [Fact]
    public async Task LoadEvents_SeveralTypes_ReturnsEventsOfAnyOfThem()
    {
        var tag = NewTag();
        var other = NewTag();
        await Append(AnyType(tag, other),
            On(new ItemAdded("apple"), tag),
            On(new ItemTransferred(tag.Id, other.Id), tag, other),
            On(new ItemRemoved("apple"), tag));

        var result = await Load(OfTypes([tag], typeof(ItemAdded), typeof(ItemRemoved)));

        Assert.Equal([new ItemAdded("apple"), new ItemRemoved("apple")], result.Events);
    }

    [Fact]
    public async Task LoadEvents_EventCarryingAnyOfTheTags_Matches()
    {
        var first = NewTag();
        var second = NewTag();
        await Append(AnyType(first, second), On(new ItemAdded("apple"), first), On(new ItemAdded("pear"), second));

        var result = await Load(AnyType(first, second));

        Assert.Equal([new ItemAdded("apple"), new ItemAdded("pear")], result.Events);
    }

    [Fact]
    public async Task LoadEvents_EventCarryingSeveralRequestedTags_IsReturnedOnce()
    {
        var from = NewTag();
        var to = NewTag();
        await Append(AnyType(from, to), On(new ItemTransferred(from.Id, to.Id), from, to));

        var result = await Load(AnyType(from, to));

        Assert.Equal([new ItemTransferred(from.Id, to.Id)], result.Events);
    }

    [Fact]
    public async Task LoadEvents_IsolatesByTenant()
    {
        var tag = NewTag();
        await Append(AnyType(tag), On(new ItemAdded("apple"), tag));

        var result = await Load(AnyType(tag), NewTenant());

        Assert.Empty(result.Events);
        Assert.Equal(0, result.Pointer);
    }

    [Fact]
    public async Task LoadEvents_IsolatesByTagName()
    {
        var id = Guid.NewGuid().ToString();
        var item = NewTag("item", id);
        await Append(AnyType(item), On(new ItemAdded("apple"), item));

        Assert.Empty((await Load(AnyType(NewTag("store", id)))).Events);
    }

    [Fact]
    public async Task LoadEvents_DoesNotCrossPairNamesAndIds()
    {
        var itemA = NewTag("item");
        var storeB = NewTag("store");
        var itemB = NewTag("item", storeB.Id);
        await Append(AnyType(itemB), On(new ItemAdded("apple"), itemB));

        // Requesting ("item", A) and ("store", B) must not match ("item", B).
        Assert.Empty((await Load(AnyType(itemA, storeB))).Events);
    }

    // --- Save ---

    [Fact]
    public async Task SaveEvents_NewEvent_CanBeLoadedBack()
    {
        var tag = NewTag();

        await _store.SaveEvents(_tenant, AnyType(tag), 0, [On(new ItemAdded("apple"), tag)]);

        Assert.Equal([new ItemAdded("apple")], (await Load(AnyType(tag))).Events);
    }

    [Fact]
    public async Task SaveEvents_SecondEventOnSameTag_IsAppended()
    {
        var tag = NewTag();
        await Append(AnyType(tag), On(new ItemAdded("apple"), tag));

        await Append(AnyType(tag), On(new ItemAdded("pear"), tag));

        Assert.Equal([new ItemAdded("apple"), new ItemAdded("pear")], (await Load(AnyType(tag))).Events);
    }

    [Fact]
    public async Task SaveEvents_EventWithSeveralTags_MatchesAQueryOnEitherTag()
    {
        var from = NewTag();
        var to = NewTag();

        await Append(AnyType(from, to), On(new ItemTransferred(from.Id, to.Id), from, to));

        Assert.Equal([new ItemTransferred(from.Id, to.Id)], (await Load(AnyType(from))).Events);
        Assert.Equal([new ItemTransferred(from.Id, to.Id)], (await Load(AnyType(to))).Events);
    }

    // --- Append condition ---

    [Fact]
    public async Task SaveEvents_WithStalePointer_Throws()
    {
        var tag = NewTag();
        var stale = await Load(AnyType(tag));
        await Append(AnyType(tag), On(new ItemAdded("apple"), tag));

        await Assert.ThrowsAsync<AppendConditionFailedException>(() =>
            _store.SaveEvents(_tenant, AnyType(tag), stale.Pointer, [On(new ItemAdded("pear"), tag)]));

        Assert.Equal([new ItemAdded("apple")], (await Load(AnyType(tag))).Events);
    }

    [Fact]
    public async Task SaveEvents_EventOnAnotherTagAppendedMeanwhile_DoesNotInvalidatePointer()
    {
        var tag = NewTag();
        var other = NewTag();
        var loaded = await Load(AnyType(tag));
        await Append(AnyType(other), On(new ItemAdded("pear"), other));

        await _store.SaveEvents(_tenant, AnyType(tag), loaded.Pointer, [On(new ItemAdded("apple"), tag)]);

        Assert.Equal([new ItemAdded("apple")], (await Load(AnyType(tag))).Events);
    }

    [Fact]
    public async Task SaveEvents_EventOfAnotherTypeAppendedMeanwhile_DoesNotInvalidatePointer()
    {
        var tag = NewTag();
        var query = OfTypes([tag], typeof(ItemAdded));
        var loaded = await Load(query);
        await Append(AnyType(tag), On(new ItemRemoved("apple"), tag));

        await _store.SaveEvents(_tenant, query, loaded.Pointer, [On(new ItemAdded("pear"), tag)]);

        Assert.Equal([new ItemRemoved("apple"), new ItemAdded("pear")], (await Load(AnyType(tag))).Events);
    }

    [Fact]
    public async Task SaveEvents_WithLatestPointer_Succeeds()
    {
        var tag = NewTag();
        await Append(AnyType(tag), On(new ItemAdded("apple"), tag));
        var loaded = await Load(AnyType(tag));

        await _store.SaveEvents(_tenant, AnyType(tag), loaded.Pointer, [On(new ItemRemoved("apple"), tag)]);

        Assert.Equal([new ItemAdded("apple"), new ItemRemoved("apple")], (await Load(AnyType(tag))).Events);
    }

    // --- Validation ---

    [Fact]
    public async Task LoadEvents_QueryWithoutTags_ThrowsArgumentException()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Load(AnyType()));
    }

    [Fact]
    public async Task SaveEvents_QueryWithoutTags_ThrowsArgumentException()
    {
        var tag = NewTag();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveEvents(_tenant, AnyType(), 0, [On(new ItemAdded("apple"), tag)]));
    }

    [Fact]
    public async Task SaveEvents_EventWithoutTags_ThrowsArgumentException()
    {
        var tag = NewTag();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveEvents(_tenant, AnyType(tag), 0, [On(new ItemAdded("apple"))]));
    }

    [Fact]
    public async Task SaveEvents_EmptyNewEvents_DoesNothing()
    {
        var tag = NewTag();

        await _store.SaveEvents<TestEvent>(_tenant, AnyType(tag), 0, []);

        Assert.Empty((await Load(AnyType(tag))).Events);
    }

    // --- Dispatch ---

    [Fact]
    public async Task SaveEvents_DispatchesSavedEventsInOrder()
    {
        var handler = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([handler]));
        var tag = NewTag();

        await store.SaveEvents(_tenant, AnyType(tag), 0, [On(new ItemAdded("apple"), tag), On(new ItemRemoved("apple"), tag)]);

        Assert.Equal(
            [(_tenant, tag.Id, new ItemAdded("apple")), (_tenant, tag.Id, new ItemRemoved("apple"))],
            handler.Received);
    }

    [Fact]
    public async Task SaveEvents_EventWithSeveralTags_IsDispatchedOncePerTag()
    {
        var handler = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([handler]));
        var from = NewTag();
        var to = NewTag();
        var transferred = new ItemTransferred(from.Id, to.Id);

        await store.SaveEvents(_tenant, AnyType(from, to), 0, [On(transferred, from, to)]);

        Assert.Equal([(_tenant, from.Id, transferred), (_tenant, to.Id, transferred)], handler.Received);
    }

    [Fact]
    public async Task SaveEvents_WithStalePointer_DispatchesNothing()
    {
        var tag = NewTag();
        await Append(AnyType(tag), On(new ItemAdded("apple"), tag));
        var handler = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([handler]));

        await Assert.ThrowsAsync<AppendConditionFailedException>(() =>
            store.SaveEvents(_tenant, AnyType(tag), 0, [On(new ItemAdded("pear"), tag)]));

        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task SaveEvents_DoesNotDispatchToBaseTypeHandlers()
    {
        var handler = new BaseTypeHandler();
        var store = new EventStore(_connection, new EventBus([handler]));
        var tag = NewTag();

        await store.SaveEvents(_tenant, AnyType(tag), 0, [On(new ItemAdded("apple"), tag)]);

        Assert.Equal(0, handler.Calls);
    }
}
