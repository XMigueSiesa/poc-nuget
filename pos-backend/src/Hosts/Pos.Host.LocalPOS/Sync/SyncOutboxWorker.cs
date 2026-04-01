using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Pos.Host.LocalPOS.Auth;
using Pos.SharedKernel.Sync;

namespace Pos.Host.LocalPOS.Sync;

public sealed class SyncOptions
{
    public string CloudBaseUrl { get; init; } = "http://localhost:5200";
    public int PollingIntervalSeconds { get; init; } = 10;
    public int MaxRetryCount { get; init; } = 5;
    public int InitialRetryDelaySeconds { get; init; } = 2;
    public int CircuitBreakerFailureThreshold { get; init; } = 10;
    public int CircuitBreakerResetSeconds { get; init; } = 60;
    public int BatchSize { get; init; } = 50;
}

public sealed class SyncOutboxWorker(
    IServiceScopeFactory scopeFactory,
    IHttpClientFactory httpClientFactory,
    IOptions<SyncOptions> options,
    TokenProvider tokenProvider,
    ILogger<SyncOutboxWorker> logger) : BackgroundService
{
    private readonly SyncOptions _options = options.Value;

    // Circuit breaker is per-worker instance (singleton lifecycle via BackgroundService)
    private SyncCircuitBreaker? _circuitBreaker;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _circuitBreaker = new SyncCircuitBreaker(
            _options.CircuitBreakerFailureThreshold,
            _options.CircuitBreakerResetSeconds,
            logger);

        logger.LogInformation(
            "Sync worker started. Polling every {Interval}s to {CloudUrl}",
            _options.PollingIntervalSeconds,
            _options.CloudBaseUrl);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_circuitBreaker.IsOpen())
                {
                    logger.LogDebug("Circuit breaker open, skipping sync cycle");
                }
                else
                {
                    await ProcessPendingEntriesAsync(stoppingToken);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Unhandled error in sync outbox worker");
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

        var now = DateTimeOffset.UtcNow;
        var pending = await db.OutboxEntries
            .Where(e =>
                e.SyncedAt == null
                && e.DeadLetteredAt == null
                && (e.NextRetryAt == null || e.NextRetryAt <= now))
            .OrderBy(e => e.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (pending.Count == 0) return;

        logger.LogInformation("Found {Count} pending sync entries", pending.Count);

        var client = httpClientFactory.CreateClient("SyncClient");

        foreach (var entry in pending)
        {
            try
            {
                if (_circuitBreaker!.IsOpen()) break;

                var endpoint = entry.EntityType switch
                {
                    "Category" => $"{_options.CloudBaseUrl}/api/sync/categories",
                    "Product"  => $"{_options.CloudBaseUrl}/api/sync/products",
                    "Order"    => $"{_options.CloudBaseUrl}/api/sync/orders",
                    _          => null
                };

                if (endpoint is null)
                {
                    logger.LogWarning("Unknown entity type {Type}, skipping", entry.EntityType);
                    continue;
                }

                var token = await tokenProvider.GetTokenAsync(ct);
                var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
                {
                    Content = new StringContent(
                        entry.Payload,
                        System.Text.Encoding.UTF8,
                        "application/json")
                };

                if (token is not null)
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var response = await client.SendAsync(request, ct);

                if (response.IsSuccessStatusCode)
                {
                    db.Entry(entry).Property(e => e.SyncedAt).CurrentValue = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(ct);

                    _circuitBreaker.RecordSuccess();

                    logger.LogInformation(
                        "Synced {Type} {Id} to cloud",
                        entry.EntityType, entry.EntityId);
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync(ct);
                    var truncatedError = errorBody.Length > 500
                        ? errorBody[..500]
                        : errorBody;

                    _circuitBreaker.RecordFailure();
                    await RecordRetryOrDeadLetterAsync(db, entry, truncatedError, ct);

                    logger.LogWarning(
                        "Failed to sync {Type} {Id}: HTTP {Status}",
                        entry.EntityType, entry.EntityId, response.StatusCode);
                }
            }
            catch (HttpRequestException ex)
            {
                _circuitBreaker!.RecordFailure();
                await RecordRetryOrDeadLetterAsync(db, entry, ex.Message, ct);

                logger.LogWarning(
                    "Cloud unreachable for {Type} {Id}: {Message}. Will retry.",
                    entry.EntityType, entry.EntityId, ex.Message);
            }
        }
    }

    private async Task RecordRetryOrDeadLetterAsync(
        SyncDbContext db,
        SyncOutboxEntry entry,
        string error,
        CancellationToken ct)
    {
        var newRetryCount = entry.RetryCount + 1;

        if (newRetryCount >= _options.MaxRetryCount)
        {
            db.Entry(entry).Property(e => e.RetryCount).CurrentValue = newRetryCount;
            db.Entry(entry).Property(e => e.LastError).CurrentValue = error;
            db.Entry(entry).Property(e => e.DeadLetteredAt).CurrentValue = DateTimeOffset.UtcNow;

            logger.LogError(
                "Entry {Type} {Id} dead-lettered after {Retries} retries. Last error: {Error}",
                entry.EntityType, entry.EntityId, newRetryCount, error);
        }
        else
        {
            // Exponential backoff: 2s, 4s, 8s, 16s, … capped at 5 minutes
            var delaySeconds = Math.Min(
                _options.InitialRetryDelaySeconds * Math.Pow(2, newRetryCount - 1),
                300);

            var nextRetry = DateTimeOffset.UtcNow.AddSeconds(delaySeconds);

            db.Entry(entry).Property(e => e.RetryCount).CurrentValue = newRetryCount;
            db.Entry(entry).Property(e => e.LastError).CurrentValue = error;
            db.Entry(entry).Property(e => e.NextRetryAt).CurrentValue = nextRetry;

            logger.LogDebug(
                "Entry {Type} {Id} retry {Attempt}/{Max} scheduled at {NextRetry}",
                entry.EntityType, entry.EntityId, newRetryCount, _options.MaxRetryCount, nextRetry);
        }

        await db.SaveChangesAsync(ct);
    }
}
