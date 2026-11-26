using Sandbox.Common;
using Sandbox.Common.Commands;
using Sandbox.Common.Dsl;

namespace Sandbox.DCB.Lite.Commands;

public class CreateBuyingOrderProcessor : IProcessor<CreateBuyingOrder, CreateBuyingOrderProcessor.State>
{
    public sealed record State(bool OrderExists, IReadOnlySet<ItemId> ExistingItems);

    public string GetTenant(CreateBuyingOrder command) => command.StoreId.Value;

    public IReadOnlyList<Stream> BuildQuery(CreateBuyingOrder command) =>
        [Streams.Order(command.OrderId), ..command.NewItems.Select(newItem => Streams.Item(newItem.Id))];

    public State Rehydrate(CreateBuyingOrder command, LoadedStreams streams)
    {
        var orderExists = streams.Get(Streams.Order(command.OrderId)).Any(evt => evt switch
        {
            BuyingOrderCreated => true,
            ItemBought => false,
        });

        var existingItems = command.NewItems
            .Select(newItem => newItem.Id)
            .Where(itemId => streams.Get(Streams.Item(itemId)).Any(evt => evt switch
            {
                ItemBought => true,
            }))
            .ToHashSet();

        return new State(orderExists, existingItems);
    }

    public IEnumerable<EventStore.EventToSave<IEvent>> Execute(CreateBuyingOrder command, State state)
    {
        if (state.OrderExists)
            throw new InvalidOperationException($"Order {command.OrderId.Value} already exists in store {command.StoreId.Value}.");

        var existingItems = command.NewItems.Where(newItem => state.ExistingItems.Contains(newItem.Id)).ToList();
        if (existingItems.Count > 0)
            throw new InvalidOperationException(
                $"Items {string.Join(", ", existingItems.Select(item => item.Id.Value))} already exist in store {command.StoreId.Value}.");

        var orderTag = Streams.Order(command.OrderId).Tag;
        return
        [
            new(new BuyingOrderCreated(command.OrderId, command.NewItems.Select(newItem => newItem.Id).ToList()), [orderTag]),
            ..command.NewItems.Select(newItem => new EventStore.EventToSave<IEvent>(
                new ItemBought(newItem.Id, command.OrderId, newItem.Description, newItem.Price),
                [Streams.Item(newItem.Id).Tag, orderTag])),
        ];
    }
}
