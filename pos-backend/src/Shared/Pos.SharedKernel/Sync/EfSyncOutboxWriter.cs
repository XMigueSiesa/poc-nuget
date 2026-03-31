namespace Pos.SharedKernel.Sync;

public sealed class EfSyncOutboxWriter(SyncDbContext db) : ISyncOutboxWriter
{
    public async Task WriteAsync(string entityType, string entityId, string jsonPayload, CancellationToken ct = default)
    {
        var entry = new SyncOutboxEntry
        {
            EntityType = entityType,
            EntityId = entityId,
            Payload = jsonPayload
        };

        db.OutboxEntries.Add(entry);
        await db.SaveChangesAsync(ct);
    }
}
