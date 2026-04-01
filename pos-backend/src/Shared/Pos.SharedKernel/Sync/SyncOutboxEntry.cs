namespace Pos.SharedKernel.Sync;

public sealed record SyncOutboxEntry
{
    public Ulid Id { get; init; } = Ulid.NewUlid();
    public required string EntityType { get; init; }
    public required string EntityId { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? SyncedAt { get; init; }

    // Resilience fields
    public int RetryCount { get; init; } = 0;
    public DateTimeOffset? NextRetryAt { get; init; }
    public string? LastError { get; init; }
    public DateTimeOffset? DeadLetteredAt { get; init; }
}
