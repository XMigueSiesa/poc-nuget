namespace Pos.SharedKernel.Sync;

public interface ISyncOutboxWriter
{
    Task WriteAsync(string entityType, string entityId, string jsonPayload, CancellationToken ct = default);
}
