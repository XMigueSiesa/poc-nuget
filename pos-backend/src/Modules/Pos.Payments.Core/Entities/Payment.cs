using Pos.SharedKernel.Entities;

namespace Pos.Payments.Core.Entities;

public sealed record Payment : BaseEntity
{
    public required Ulid OrderId { get; init; }
    public required string Method { get; init; } // Cash, Card, Transfer
    public required decimal Amount { get; init; }
    public required string Status { get; init; } // Pending, Completed, Failed
    public string? TransactionId { get; init; }
}
