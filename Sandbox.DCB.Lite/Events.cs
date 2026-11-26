using System.Text.Json.Serialization;
using Sandbox.Common;
using Sandbox.Common.Dsl;

namespace Sandbox.DCB.Lite;

public sealed record BuyingOrderCreated(OrderId OrderId, List<ItemId> ItemIds) : IEvent;
public sealed record ItemBought(ItemId ItemId, OrderId OrderId, string Description, decimal Price) : IEvent;

// One union per stream: every event that can be flagged on that stream.

[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union OrderEvent(BuyingOrderCreated, ItemBought) : IEvent;

[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union ItemEvent(ItemBought) : IEvent;

public static class Streams
{
    public static Stream<OrderEvent> Order(OrderId id) => new(new EventStore.Tag("order", id.Value));
    public static Stream<ItemEvent> Item(ItemId id) => new(new EventStore.Tag("item", id.Value));
}
