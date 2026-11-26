using Sandbox.Common.Dsl;
using Sandbox.Common.Queries;

namespace Sandbox.DCB.Lite.Readmodels;

public class AvailableItems : IEventHandler<ItemBought>, IQueryHandler<GetAvailableItems, List<Item>>
{
    private readonly Dictionary<StoreId, OrderedDictionary<ItemId, Item>> _itemsByStore = new();

    public Task Handle(string tenant, string aggregateId, ItemBought @event)
    {
        var storeId = new StoreId(tenant);
        if (!_itemsByStore.TryGetValue(storeId, out var items))
        {
            items = new OrderedDictionary<ItemId, Item>();
            _itemsByStore[storeId] = items;
        }

        // Idempotent: the event store dispatches an event once per tag.
        items.TryAdd(@event.ItemId, new Item(@event.ItemId, @event.Description, @event.Price));
        return Task.CompletedTask;
    }

    public List<Item> Request(GetAvailableItems query) =>
        _itemsByStore.TryGetValue(query.StoreId, out var items) ? items.Values.ToList() : [];
}
