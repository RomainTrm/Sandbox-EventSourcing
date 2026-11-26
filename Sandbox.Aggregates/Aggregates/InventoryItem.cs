using System.Text.Json.Serialization;
using Sandbox.Common;
using Sandbox.Common.Dsl;

namespace Sandbox.Aggregates.Aggregates;

public sealed record ItemBought(ItemId ItemId, OrderId OrderId, string Description, decimal Price) : IEvent;

[JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
public union ItemEvent(ItemBought) : IEvent;

public class ItemAggregate
{
    public const string Name = "item";

    public sealed record State(ItemId Id, bool Exists)
    {
        public static State Initial(ItemId id) => new(id, false);

        public State Apply(ItemEvent evt) => evt switch
        {
            ItemBought => this with { Exists = true },
        };
    }

    private readonly State _state;

    private ItemAggregate(State state) => _state = state;

    public static ItemAggregate Rehydrate(ItemId id, IEnumerable<ItemEvent> events)
        => new(events.Aggregate(State.Initial(id), (state, evt) => state.Apply(evt)));

    public List<ItemEvent> Buy(OrderId orderId, string description, decimal price)
    {
        if (_state.Exists) throw new InvalidOperationException($"Item {_state.Id.Value} already exists.");
        return [new ItemBought(_state.Id, orderId, description, price)];
    }
}
