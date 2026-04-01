namespace Pos.SharedKernel.Auth;

public sealed record StoreCredential
{
    public Ulid Id { get; init; } = Ulid.NewUlid();
    public required string StoreId { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecretHash { get; init; }
    public required string StoreName { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
