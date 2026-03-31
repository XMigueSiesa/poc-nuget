using Pos.SharedKernel.Entities;

namespace Pos.Products.Core.Entities;

public sealed record Product : BaseEntity
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required Ulid CategoryId { get; init; }
    public Category? Category { get; init; }
    public required decimal Price { get; init; }
    public bool IsActive { get; init; } = true;
}
