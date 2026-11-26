using Sandbox.Common;
using Sandbox.Common.Commands;
using Sandbox.Common.Database;

namespace Sandbox.DCB.Full.Commands;

public class CreateBuyingOrderHandler(IConnectionFactory connectionFactory, EventBus eventBus) : ICommandHandler<CreateBuyingOrder>
{
    public async Task Handle(CreateBuyingOrder command)
    {
        var eventStore = await EventStore.Build(connectionFactory, eventBus);
        var processor = new CreateBuyingOrderProcessor();
        await processor.Process(eventStore, command);
    }
}
