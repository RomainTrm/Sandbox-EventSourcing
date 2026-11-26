using Sandbox.Common.Dsl;

namespace Sandbox.Common.Commands;

public record CreateBuyingOrder(StoreId StoreId, OrderId OrderId, List<NewItem> NewItems) : ICommand;

public record NewItem(ItemId Id, string Description, decimal Price);
