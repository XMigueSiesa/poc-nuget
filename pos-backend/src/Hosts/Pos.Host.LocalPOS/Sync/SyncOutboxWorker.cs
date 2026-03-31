using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pos.SharedKernel.Sync;

namespace Pos.Host.LocalPOS.Sync;

public sealed class SyncOptions
{
    public string CloudBaseUrl { get; init; } = "http://localhost:5200";
    public int PollingIntervalSeconds { get; init; } = 10;
}

public sealed class SyncOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<SyncOptions> options,
    ILogger<SyncOutboxWorker> logger) : BackgroundService
{
    private readonly SyncOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Sync worker started. Polling every {Interval}s to {CloudUrl}",
            _options.PollingIntervalSeconds,
            _options.CloudBaseUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingEntriesAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Error processing sync outbox entries");
            }

            await Task.Delay(
                TimeSpan.FromSeconds(_options.PollingIntervalSeconds),
                stoppingToken);
        }
    }

    private async Task ProcessPendingEntriesAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SyncDbContext>();

        var pending = await db.OutboxEntries
            .Where(e => e.SyncedAt == null)
            .OrderBy(e => e.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        logger.LogInformation("Found {Count} pending sync entries", pending.Count);

        var client = httpClientFactory.CreateClient("SyncClient");

        foreach (var entry in pending)
        {
            try
            {
                var endpoint = entry.EntityType switch
                {
                    "Product" => $"{_options.CloudBaseUrl}/api/sync/products",
                    "Order" => $"{_options.CloudBaseUrl}/api/sync/orders",
                    _ => null
                };

                if (endpoint is null)
                {
                    logger.LogWarning("Unknown entity type {Type}, skipping", entry.EntityType);
                    continue;
                }

                var content = new StringContent(entry.Payload, System.Text.Encoding.UTF8, "application/json");
                var response = await client.PostAsync(endpoint, content, ct);

                if (response.IsSuccessStatusCode)
                {
                    db.Entry(entry).Property(e => e.SyncedAt).CurrentValue = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);

                    logger.LogInformation(
                        "Synced {Type} {Id} to cloud",
                        entry.EntityType, entry.EntityId);
                }
                else
                {
                    logger.LogWarning(
                        "Failed to sync {Type} {Id}: {Status}",
                        entry.EntityType, entry.EntityId, response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex,
                    "Cloud unreachable for {Type} {Id}, will retry next cycle",
                    entry.EntityType, entry.EntityId);
            }
        }
    }
}
