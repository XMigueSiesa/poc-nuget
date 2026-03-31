using Pos.SharedKernel.Entities;

namespace Pos.Orders.Core.Entities;

public sealed record Order : BaseEntity
{
    public required string Status { get; init; } // Open, Closed, Cancelled
    public string? TableNumber { get; init; }
    public List<OrderLine> Lines { get; init; } = [];
    public decimal Total { get; init; }
}

public sealed record OrderLine : BaseEntity
{
    public required Ulid OrderId { get; init; }
    public required string ProductId { get; init; }
    public required string ProductName { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public decimal LineTotal => Quantity * UnitPrice;
}
