using Pos.SharedKernel.Entities;

namespace Pos.Products.Core.Entities;

public sealed record Category : BaseEntity
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public List<Product> Products { get; init; } = [];
}
