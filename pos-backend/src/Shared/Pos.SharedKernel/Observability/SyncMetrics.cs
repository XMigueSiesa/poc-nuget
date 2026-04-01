using System.Diagnostics.Metrics;

namespace Pos.SharedKernel.Observability;

/// <summary>
/// Custom OpenTelemetry metrics for the Outbox sync subsystem.
/// Consumed by both LocalPOS (emitting) and CloudHub (receiving).
/// </summary>
public sealed class SyncMetrics : IDisposable
{
    public const string MeterName = "Pos.Sync";

    private readonly Meter _meter;

    // Outbox write side
    public readonly Counter<long> EntriesWritten;

    // Sync worker side
    public readonly Counter<long> EntriesSynced;
    public readonly Counter<long> EntriesFailed;
    public readonly Counter<long> EntriesDeadLettered;
    public readonly Histogram<double> SyncDurationMs;
    public readonly UpDownCounter<long> PendingEntries;

    public SyncMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        EntriesWritten = _meter.CreateCounter<long>(
            "pos.sync.entries_written",
            unit: "{entry}",
            description: "Total outbox entries written");

        EntriesSynced = _meter.CreateCounter<long>(
            "pos.sync.entries_synced",
            unit: "{entry}",
            description: "Total outbox entries successfully synced to cloud");

        EntriesFailed = _meter.CreateCounter<long>(
            "pos.sync.entries_failed",
            unit: "{entry}",
            description: "Total outbox sync attempts that failed (will retry)");

        EntriesDeadLettered = _meter.CreateCounter<long>(
            "pos.sync.entries_dead_lettered",
            unit: "{entry}",
            description: "Total outbox entries moved to dead-letter");

        SyncDurationMs = _meter.CreateHistogram<double>(
            "pos.sync.duration_ms",
            unit: "ms",
            description: "Duration of individual sync HTTP calls");

        PendingEntries = _meter.CreateUpDownCounter<long>(
            "pos.sync.pending_entries",
            unit: "{entry}",
            description: "Current number of pending outbox entries");
    }

    public void Dispose() => _meter.Dispose();
}
