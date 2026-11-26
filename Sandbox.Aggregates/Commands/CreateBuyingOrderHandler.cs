using Sandbox.Aggregates.Aggregates;
using Sandbox.Common;
using Sandbox.Common.Commands;
using Sandbox.Common.Database;

namespace Sandbox.Aggregates.Commands;

public class CreateBuyingOrderHandler(IConnectionFactory connectionFactory, EventBus eventBus) : ICommandHandler<CreateBuyingOrder>
{
    private readonly Task<EventStore> _eventStore = EventStore.Build(connectionFactory, eventBus);

    public async Task Handle(CreateBuyingOrder command)
    {
        var eventStore = await _eventStore;
        var tenant = command.StoreId.Value;

        var orderQuery = new EventStore.Query(tenant, OrderAggregate.Name, command.OrderId.Value);
        var orderEvents = await eventStore.LoadEvents<OrderEvent>(orderQuery);
        var order = OrderAggregate.Rehydrate(command.OrderId, orderEvents);
        var newOrderEvents = order.CreateBuyingOrder(command.NewItems.Select(newItem => newItem.Id).ToList());

        List<EventStore.SaveRequest> saveRequests = [new EventStore.SaveRequest<OrderEvent>(orderQuery, orderEvents.Count, newOrderEvents)];

        foreach (var newItem in command.NewItems)
        {
            var itemQuery = new EventStore.Query(tenant, ItemAggregate.Name, newItem.Id.Value);
            var itemEvents = await eventStore.LoadEvents<ItemEvent>(itemQuery);
            var item = ItemAggregate.Rehydrate(newItem.Id, itemEvents);
            var newItemEvents = item.Buy(command.OrderId, newItem.Description, newItem.Price);
            saveRequests.Add(new EventStore.SaveRequest<ItemEvent>(itemQuery, itemEvents.Count, newItemEvents));
        }

        await eventStore.SaveEvents(saveRequests);
    }
}
