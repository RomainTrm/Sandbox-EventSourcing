using System.Text.Json.Serialization;
using Sandbox.Common;
using Sandbox.Common.Dsl;

namespace Sandbox.Aggregates.Aggregates;

public sealed record BuyingOrderCreated(OrderId OrderId, List<ItemId> ItemIds) : IEvent;

[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union OrderEvent(BuyingOrderCreated) : IEvent;

public class OrderAggregate
{
    public const string Name = "order";

    public sealed record State(OrderId Id, bool Exists)
    {
        public static State Initial(OrderId id) => new(id, false);

        public State Apply(OrderEvent evt) => evt switch
        {
            BuyingOrderCreated => this with { Exists = true },
        };
    }

    private readonly State _state;

    private OrderAggregate(State state) => _state = state;

    public static OrderAggregate Rehydrate(OrderId id, IEnumerable<OrderEvent> events)
        => new(events.Aggregate(State.Initial(id), (state, evt) => state.Apply(evt)));

    public List<OrderEvent> CreateBuyingOrder(List<ItemId> itemIds)
    {
        if (_state.Exists) throw new InvalidOperationException($"Order {_state.Id.Value} already exists.");
        return [new BuyingOrderCreated(_state.Id, itemIds)];
    }
}
