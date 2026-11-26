using Sandbox.Common.Dsl;

namespace Sandbox.Common.Queries;

public class Item(ItemId id, string description, decimal price)
{
    public ItemId Id { get; } = id;
    public string Description { get; } = description;
    public decimal Price { get; } = price;
}