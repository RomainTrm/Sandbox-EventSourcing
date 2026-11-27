using System.Text.Json.Serialization;
using Sandbox.Common;
using Sandbox.Common.Commands;
using Sandbox.Common.Dsl;

namespace Sandbox.DCB.Full.Commands;

public class CreateBuyingOrderDecider(CreateBuyingOrder command) : IDecider<CreateBuyingOrderDecider.Event, CreateBuyingOrderDecider.State>
{
    /// <summary>The events this decision reads.</summary>
    [JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
    public union Event(BuyingOrderCreated, ItemBought) : IEvent;

    public sealed record State(bool OrderExists, IReadOnlySet<ItemId> ExistingItems) : IDeciderState<State, Event>
    {
        public static State InitialState => new(false, new HashSet<ItemId>());

        public State ApplyEvent(Event evt) => evt switch
        {
            BuyingOrderCreated => this with { OrderExists = true },
            ItemBought bought => this with { ExistingItems = new HashSet<ItemId>(ExistingItems) { bought.ItemId } },
        };
    }

    public string GetTenant() => command.StoreId.Value;

    public IReadOnlyList<EventStore.Tag> BuildQuery() =>
        [Tags.Order(command.OrderId), ..command.NewItems.Select(newItem => Tags.Item(newItem.Id))];

    public IEnumerable<EventStore.EventToSave<IEvent>> Decide(State state)
    {
        if (state.OrderExists)
            throw new InvalidOperationException($"Order {command.OrderId.Value} already exists in store {command.StoreId.Value}.");

        var existingItems = command.NewItems.Where(newItem => state.ExistingItems.Contains(newItem.Id)).ToList();
        if (existingItems.Count > 0)
            throw new InvalidOperationException($"Items {string.Join(", ", existingItems.Select(item => item.Id.Value))} already exist in store {command.StoreId.Value}.");

        var orderTag = Tags.Order(command.OrderId);
        return
        [
            new(new BuyingOrderCreated(command.OrderId, command.NewItems.Select(newItem => newItem.Id).ToList()), [orderTag]),
            ..command.NewItems.Select(newItem => new EventStore.EventToSave<IEvent>(
                new ItemBought(newItem.Id, command.OrderId, newItem.Description, newItem.Price),
                [Tags.Item(newItem.Id), orderTag])),
        ];
    }
}
