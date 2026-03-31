namespace Pos.SharedKernel.Entities;

public abstract record BaseEntity
{
    public Ulid Id { get; init; } = Ulid.NewUlid();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; init; }
}
