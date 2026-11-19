using Sandbox.Common.Commands;
using Sandbox.Common.Database;

namespace Sandbox.Aggregates.Commands;

public class CreateBuyingOrderHandler(IConnectionFactory connectionFactory) : ICommandHandler<CreateBuyingOrder>
{
    private readonly Task<EventStore> _eventStore = EventStore.Build(connectionFactory);

    public Task Handle(CreateBuyingOrder command)
    {
        throw new NotImplementedException();
    }
}