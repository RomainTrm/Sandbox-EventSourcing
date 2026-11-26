using Sandbox.Common.Dsl;

namespace Sandbox.Common.Queries;

public record GetAvailableItems(StoreId StoreId) : IQuery<List<Item>>;