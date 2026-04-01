using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pos.SharedKernel.Sync;

namespace Pos.SharedKernel.Health;

/// <summary>
/// Reports sync backlog size as a health signal.
/// Healthy: 0–99 pending entries.
/// Degraded: 100–499 pending entries.
/// Unhealthy: 500+ pending entries.
/// </summary>
public sealed class OutboxBacklogHealthCheck(SyncDbContext db) : IHealthCheck
{
    private const int DegradedThreshold = 100;
    private const int UnhealthyThreshold = 500;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var pendingCount = await db.OutboxEntries
            .CountAsync(
                e => e.SyncedAt == null
                  && e.DeadLetteredAt == null
                  && (e.NextRetryAt == null || e.NextRetryAt <= now),
                cancellationToken);

        var deadLetterCount = await db.OutboxEntries
            .CountAsync(e => e.DeadLetteredAt != null, cancellationToken);

        var data = new Dictionary<string, object>
        {
            ["pending_entries"] = pendingCount,
            ["dead_letter_entries"] = deadLetterCount
        };

        if (pendingCount >= UnhealthyThreshold)
            return HealthCheckResult.Unhealthy(
                $"Sync backlog critical: {pendingCount} entries pending", data: data);

        if (pendingCount >= DegradedThreshold || deadLetterCount > 0)
            return HealthCheckResult.Degraded(
                $"Sync backlog elevated: {pendingCount} pending, {deadLetterCount} dead-lettered",
                data: data);

        return HealthCheckResult.Healthy(
            $"Sync backlog normal: {pendingCount} pending", data: data);
    }
}
