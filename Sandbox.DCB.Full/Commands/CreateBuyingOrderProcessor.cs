using System.Text.Json.Serialization;
using Sandbox.Common;
using Sandbox.Common.Commands;
using Sandbox.Common.Dsl;

namespace Sandbox.DCB.Full.Commands;

public class CreateBuyingOrderProcessor
    : IProcessor<CreateBuyingOrder, CreateBuyingOrderProcessor.Event, CreateBuyingOrderProcessor.State>
{
    /// <summary>The events this decision reads.</summary>
    [JsonUnion(TypeClassifier = typeof(JsonUnionTypeStructuralClassifier))]
    public union Event(BuyingOrderCreated, ItemBought) : IEvent;

    public sealed record State(bool OrderExists, IReadOnlySet<ItemId> ExistingItems);

    public string GetTenant(CreateBuyingOrder command) => command.StoreId.Value;

    public IReadOnlyList<EventStore.Tag> BuildQuery(CreateBuyingOrder command) =>
        [Tags.Order(command.OrderId), ..command.NewItems.Select(newItem => Tags.Item(newItem.Id))];

    public State InitialState => new(false, new HashSet<ItemId>());

    // The query only loads BuyingOrderCreated events tagged with this command's order, so any of them means the order exists.
    public State ApplyEvent(State state, Event evt) => evt switch
    {
        BuyingOrderCreated => state with { OrderExists = true },
        ItemBought bought => state with { ExistingItems = new HashSet<ItemId>(state.ExistingItems) { bought.ItemId } },
    };

    public IEnumerable<EventStore.EventToSave<IEvent>> Execute(CreateBuyingOrder command, State state)
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
