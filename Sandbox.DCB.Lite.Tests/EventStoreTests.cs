using Sandbox.Common;
using Sandbox.Common.Database;
using Sandbox.DCB.Lite.Tests.Fakes;
using static Sandbox.DCB.Lite.EventStore;

namespace Sandbox.DCB.Lite.Tests;

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
    private static TagQuery New(Tag tag) => new(tag, 0);
    private static EventToSave<TestEvent> On(TestEvent evt, params Tag[] tags) => new(evt, [..tags]);

    private async Task<List<TestEvent>> Stream(Tag tag, string? tenant = null)
    {
        var result = await _store.LoadEvents<TestEvent>(tenant ?? _tenant, [tag]);
        return Assert.Single(result).Value;
    }

    private async Task<int> Version(Tag tag)
    {
        var result = await _store.LoadEvents<TestEvent>(_tenant, [tag]);
        return Assert.Single(result).Key.Version;
    }

    // --- Load ---

    [Fact]
    public async Task LoadEvents_UnknownTag_ReturnsVersion0AndNoEvents()
    {
        var tag = NewTag();

        var result = await _store.LoadEvents<TestEvent>(_tenant, [tag]);

        var (query, events) = Assert.Single(result);
        Assert.Equal(new TagQuery(tag, 0), query);
        Assert.Empty(events);
    }

    [Fact]
    public async Task LoadEvents_NoTags_ReturnsEmpty()
    {
        Assert.Empty(await _store.LoadEvents<TestEvent>(_tenant, []));
    }

    [Fact]
    public async Task SaveEvents_NewTag_EventsCanBeLoadedBack()
    {
        var tag = NewTag();

        await _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag)]);

        Assert.Equal([new ItemAdded("apple")], await Stream(tag));
    }

    [Fact]
    public async Task LoadEvents_ReturnsEventsInSavedOrder()
    {
        var tag = NewTag();

        await _store.SaveEvents(_tenant, [New(tag)], [
            On(new ItemAdded("apple"), tag),
            On(new ItemAdded("pear"), tag),
            On(new ItemRemoved("apple"), tag),
        ]);

        Assert.Equal([new ItemAdded("apple"), new ItemAdded("pear"), new ItemRemoved("apple")], await Stream(tag));
    }

    [Fact]
    public async Task LoadEvents_SameTagTwice_ReturnsItOnce()
    {
        var tag = NewTag();
        await _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag)]);

        var result = await _store.LoadEvents<TestEvent>(_tenant, [tag, tag]);

        var (query, events) = Assert.Single(result);
        Assert.Equal(new TagQuery(tag, 1), query);
        Assert.Equal([new ItemAdded("apple")], events);
    }

    // --- Versions ---

    [Fact]
    public async Task LoadEvents_VersionIsTheNumberOfEventsOfTheTag()
    {
        var tag = NewTag();

        await _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag), On(new ItemAdded("pear"), tag)]);

        Assert.Equal(2, await Version(tag));
    }

    [Fact]
    public async Task SaveEvents_ExistingTag_AppendsWithCurrentVersion()
    {
        var tag = NewTag();
        await _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag), On(new ItemAdded("pear"), tag)]);
        var loaded = await _store.LoadEvents<TestEvent>(_tenant, [tag]);

        await _store.SaveEvents(_tenant, [..loaded.Keys], [On(new ItemRemoved("apple"), tag)]);

        Assert.Equal([new ItemAdded("apple"), new ItemAdded("pear"), new ItemRemoved("apple")], await Stream(tag));
        Assert.Equal(3, await Version(tag));
    }

    // --- Multi-tag ---

    [Fact]
    public async Task SaveEvents_EventWithSeveralTags_AppearsInEveryStream()
    {
        var from = NewTag();
        var to = NewTag();
        await _store.SaveEvents(_tenant, [New(from)], [On(new ItemAdded("apple"), from)]);

        await _store.SaveEvents(_tenant, [new TagQuery(from, 1), New(to)], [On(new ItemTransferred(from.Id, to.Id), from, to)]);

        var result = await _store.LoadEvents<TestEvent>(_tenant, [from, to]);
        Assert.Equal([new ItemAdded("apple"), new ItemTransferred(from.Id, to.Id)], result[new TagQuery(from, 2)]);
        Assert.Equal([new ItemTransferred(from.Id, to.Id)], result[new TagQuery(to, 1)]);
    }

    [Fact]
    public async Task LoadEvents_SeveralTags_ReturnsEachTagOwnStream()
    {
        var first = NewTag();
        var second = NewTag();
        var unknown = NewTag();
        await _store.SaveEvents(_tenant, [New(first), New(second)], [
            On(new ItemAdded("apple"), first),
            On(new ItemAdded("pear"), second),
            On(new ItemRemoved("pear"), second),
        ]);

        var result = await _store.LoadEvents<TestEvent>(_tenant, [first, second, unknown]);

        Assert.Equal(3, result.Count);
        Assert.Equal([new ItemAdded("apple")], result[new TagQuery(first, 1)]);
        Assert.Equal([new ItemAdded("pear"), new ItemRemoved("pear")], result[new TagQuery(second, 2)]);
        Assert.Empty(result[new TagQuery(unknown, 0)]);
    }

    [Fact]
    public async Task SaveEvents_DuplicateTagOnOneEvent_IsFlaggedOnce()
    {
        var tag = NewTag();

        await _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag, tag)]);

        Assert.Equal([new ItemAdded("apple")], await Stream(tag));
        Assert.Equal(1, await Version(tag));
    }

    // --- Concurrency ---

    [Fact]
    public async Task SaveEvents_WithStaleVersion_Throws()
    {
        var tag = NewTag();
        await _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag)]);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("pear"), tag)]));
    }

    [Fact]
    public async Task SaveEvents_WithStaleVersionOnOneOfSeveralTags_Throws()
    {
        // A failed insert aborts the shared test transaction, so the rollback itself can't be observed here.
        var stale = NewTag();
        var fresh = NewTag();
        await _store.SaveEvents(_tenant, [New(stale)], [On(new ItemAdded("apple"), stale)]);

        await Assert.ThrowsAnyAsync<Exception>(() =>
            _store.SaveEvents(_tenant, [New(fresh), New(stale)], [On(new ItemTransferred(stale.Id, fresh.Id), fresh, stale)]));
    }

    [Fact]
    public async Task SaveEvents_TagWithoutExpectedVersion_ThrowsArgumentException()
    {
        var known = NewTag();
        var unknown = NewTag();

        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveEvents(_tenant, [New(known)], [On(new ItemTransferred(known.Id, unknown.Id), known, unknown)]));

        Assert.Empty(await Stream(known));
        Assert.Empty(await Stream(unknown));
    }

    // --- Isolation ---

    [Fact]
    public async Task LoadEvents_IsolatesByTenant()
    {
        var tag = NewTag();

        await _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag)]);

        Assert.Equal([new ItemAdded("apple")], await Stream(tag));
        Assert.Empty(await Stream(tag, NewTenant()));
    }

    [Fact]
    public async Task LoadEvents_IsolatesByTagName()
    {
        var id = Guid.NewGuid().ToString();
        var item = NewTag("item", id);
        var store = NewTag("store", id);

        await _store.SaveEvents(_tenant, [New(item)], [On(new ItemAdded("apple"), item)]);

        Assert.Equal([new ItemAdded("apple")], await Stream(item));
        Assert.Empty(await Stream(store));
    }

    [Fact]
    public async Task SaveEvents_EmptyNewEvents_DoesNothing()
    {
        var tag = NewTag();

        await _store.SaveEvents<TestEvent>(_tenant, [New(tag)], []);

        Assert.Empty(await Stream(tag));
    }

    // --- Dispatch ---

    [Fact]
    public async Task SaveEvents_DispatchesSavedEventsInOrder()
    {
        var handler = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([handler]));
        var tag = NewTag();

        await store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag), On(new ItemRemoved("apple"), tag)]);

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

        await store.SaveEvents(_tenant, [New(from), New(to)], [On(transferred, from, to)]);

        Assert.Equal([(_tenant, from.Id, transferred), (_tenant, to.Id, transferred)], handler.Received);
    }

    [Fact]
    public async Task SaveEvents_DispatchesToEveryHandlerOfTheSameEventType()
    {
        var first = new RecordingHandler();
        var second = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([first, second]));
        var tag = NewTag();

        await store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag)]);

        Assert.Single(first.Received);
        Assert.Single(second.Received);
    }

    [Fact]
    public async Task SaveEvents_WithStaleVersion_DispatchesNothing()
    {
        var tag = NewTag();
        await _store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag)]);
        var handler = new RecordingHandler();
        var store = new EventStore(_connection, new EventBus([handler]));

        await Assert.ThrowsAnyAsync<Exception>(() =>
            store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("pear"), tag)]));

        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task SaveEvents_DoesNotDispatchToBaseTypeHandlers()
    {
        var handler = new BaseTypeHandler();
        var store = new EventStore(_connection, new EventBus([handler]));
        var tag = NewTag();

        await store.SaveEvents(_tenant, [New(tag)], [On(new ItemAdded("apple"), tag)]);

        Assert.Equal(0, handler.Calls);
    }
}
