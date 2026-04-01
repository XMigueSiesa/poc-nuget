using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace Pos.Host.LocalPOS.Auth;

public sealed class AuthOptions
{
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string TokenEndpoint { get; init; } = "http://localhost:5200/api/auth/token";
}

public sealed class TokenProvider : IDisposable
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AuthOptions _options;
    private readonly ILogger<TokenProvider> _logger;
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry = DateTimeOffset.MinValue;

    public TokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<AuthOptions> options,
        ILogger<TokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<string?> GetTokenAsync(CancellationToken ct)
    {
        if (IsTokenValid())
            return _cachedToken;

        await _semaphore.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (IsTokenValid())
                return _cachedToken;

            return await RefreshTokenAsync(ct);
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private bool IsTokenValid() =>
        _cachedToken is not null
        && DateTimeOffset.UtcNow.AddMinutes(5) < _tokenExpiry;

    private async Task<string?> RefreshTokenAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient("SyncClient");

            var payload = new { client_id = _options.ClientId, client_secret = _options.ClientSecret };
            var response = await client.PostAsJsonAsync(_options.TokenEndpoint, payload, ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Token request failed with status {Status}",
                    response.StatusCode);
                return null;
            }

            var tokenResponse = await response.Content
                .ReadFromJsonAsync<TokenResponseDto>(ct);

            if (tokenResponse is null || string.IsNullOrEmpty(tokenResponse.AccessToken))
            {
                _logger.LogWarning("Token response was empty or malformed");
                return null;
            }

            _cachedToken = tokenResponse.AccessToken;
            _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

            _logger.LogInformation(
                "Token refreshed, expires at {Expiry}",
                _tokenExpiry);

            return _cachedToken;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to refresh authentication token");
            return null;
        }
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed record TokenResponseDto(
        string? AccessToken,
        string? TokenType,
        int ExpiresIn);
}
