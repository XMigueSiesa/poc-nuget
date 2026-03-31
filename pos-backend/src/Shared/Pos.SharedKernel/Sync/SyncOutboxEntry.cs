namespace Pos.SharedKernel.Sync;

public sealed record SyncOutboxEntry
{
    public Ulid Id { get; init; } = Ulid.NewUlid();
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; init; }
}
