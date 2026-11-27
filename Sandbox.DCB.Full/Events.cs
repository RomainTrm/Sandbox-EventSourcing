using Sandbox.Common;
using Sandbox.Common.Dsl;

namespace Sandbox.DCB.Full;

// Events are plain records. Each decider reads them through its own union of the events it requests.
public sealed record BuyingOrderCreated(OrderId OrderId, List<ItemId> ItemIds) : IEvent;
public sealed record ItemBought(ItemId ItemId, OrderId OrderId, string Description, decimal Price) : IEvent;

public static class Tags
{
    public static EventStore.Tag Order(OrderId id) => new("order", id.Value);
    public static EventStore.Tag Item(ItemId id) => new("item", id.Value);
}
