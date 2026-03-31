using Pos.SharedKernel.Sync;

namespace Pos.Tests.SharedKernel;

public sealed class SyncOutboxEntryTests
{
    [Fact]
    public void NewEntry_ShouldHaveGeneratedId()
    {
        var entry = new SyncOutboxEntry
        {
            EntityType = "Product",
            EntityId = "test-id",
            Payload = "{}"
        };

        entry.Id.Should().NotBe(default(Ulid));
    }

    [Fact]
    public void NewEntry_ShouldHaveCreatedAtSet()
    {
        var before = DateTimeOffset.UtcNow;

        var entry = new SyncOutboxEntry
        {
            EntityType = "Product",
            EntityId = "test-id",
            Payload = "{}"
        };

        entry.CreatedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void NewEntry_ShouldHaveSyncedAtNull()
    {
        var entry = new SyncOutboxEntry
        {
            EntityType = "Product",
            EntityId = "test-id",
            Payload = "{}"
        };

        entry.SyncedAt.Should().BeNull();
    }

    [Fact]
    public void Entry_ShouldBeImmutable_WithExpression()
    {
        var original = new SyncOutboxEntry
        {
            EntityType = "Product",
            EntityId = "test-id",
            Payload = "{}"
        };

        var synced = original with { SyncedAt = DateTimeOffset.UtcNow };

        original.SyncedAt.Should().BeNull();
        synced.SyncedAt.Should().NotBeNull();
        synced.Id.Should().Be(original.Id);
    }
}
