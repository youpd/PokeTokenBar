using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Limits;

public sealed class ClaudeLimitsProvider : IClaudeLimitsProvider, IDisposable
{
    public static readonly Uri DefaultEndpoint = new(
        "https://api.anthropic.com/api/oauth/usage");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly string _credentialsFile;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Uri _endpoint;
    private Credential? _cachedCredential;
    private DateTimeOffset? _backoffUntil;
    private TimeSpan _nextBackoff = TimeSpan.FromMinutes(5);

    public ClaudeLimitsProvider(
        string? credentialsFile = null,
        HttpClient? httpClient = null,
        Func<DateTimeOffset>? clock = null,
        Uri? endpoint = null)
    {
        _credentialsFile = credentialsFile ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude",
            ".credentials.json");
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _endpoint = endpoint ?? DefaultEndpoint;
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    public DateTimeOffset? BackoffUntil => _backoffUntil;

    public async Task<ClaudeLimitStatus> FetchAsync(
        bool forceCredentialReload = false,
        CancellationToken cancellationToken = default)
    {
        var now = _clock();
        if (!forceCredentialReload && _backoffUntil is { } backoff && now < backoff)
        {
            throw new ClaudeLimitsException(
                "Claude limits are in backoff.",
                retryAfter: backoff - now);
        }

        if (forceCredentialReload)
        {
            _cachedCredential = null;
            _backoffUntil = null;
        }

        var credential = LoadCredential();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                credential.AccessToken);
            request.Headers.TryAddWithoutValidation(
                "anthropic-beta",
                "oauth-2025-04-20");

            using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken)
                .ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                _cachedCredential = null;
                if (attempt == 0)
                {
                    credential = LoadCredential();
                    continue;
                }

                throw new ClaudeLimitsException(
                    "Claude credentials are expired. Run Claude Code and retry.",
                    isAuthenticationExpired: true);
            }

            if ((int)response.StatusCode == 429)
            {
                var retryAfter = ReadRetryAfter(response) ?? _nextBackoff;
                retryAfter = TimeSpan.FromSeconds(Math.Min(3600, retryAfter.TotalSeconds));
                _backoffUntil = _clock().Add(retryAfter);
                _nextBackoff = TimeSpan.FromSeconds(Math.Min(
                    3600,
                    Math.Max(300, _nextBackoff.TotalSeconds * 2)));
                throw new ClaudeLimitsException(
                    "Claude limits were rate limited.",
                    retryAfter: retryAfter);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ClaudeLimitsException(
                    $"Claude limits request failed ({(int)response.StatusCode}).");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var limits = await JsonSerializer.DeserializeAsync<ClaudeLimitStatus>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                .ConfigureAwait(false)
                ?? throw new ClaudeLimitsException("Claude limits response was empty.");

            _backoffUntil = null;
            _nextBackoff = TimeSpan.FromMinutes(5);
            return limits with
            {
                SubscriptionType = credential.SubscriptionType,
                RateLimitTier = credential.RateLimitTier,
            };
        }

        throw new ClaudeLimitsException(
            "Claude credentials are expired.",
            isAuthenticationExpired: true);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private Credential LoadCredential()
    {
        if (_cachedCredential is { } cached && !IsExpired(cached.ExpiresAt))
        {
            return cached;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(_credentialsFile));
            if (!document.RootElement.TryGetProperty("claudeAiOauth", out var oauth))
            {
                throw new ClaudeLimitsException(
                    "Claude credentials are unavailable.",
                    isAuthenticationExpired: true);
            }

            var token = oauth.TryGetProperty("accessToken", out var tokenElement)
                ? tokenElement.GetString()
                : null;
            var expiresAt = oauth.TryGetProperty("expiresAt", out var expiryElement)
                ? ReadExpiry(expiryElement)
                : null;
            if (string.IsNullOrWhiteSpace(token) || IsExpired(expiresAt))
            {
                throw new ClaudeLimitsException(
                    "Claude credentials are expired. Run Claude Code and retry.",
                    isAuthenticationExpired: true);
            }

            _cachedCredential = new Credential(
                token,
                expiresAt,
                ReadString(oauth, "subscriptionType"),
                ReadString(oauth, "rateLimitTier"));
            return _cachedCredential;
        }
        catch (ClaudeLimitsException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new ClaudeLimitsException(
                "Claude credentials are unavailable.",
                isAuthenticationExpired: true,
                innerException: exception);
        }
    }

    private bool IsExpired(DateTimeOffset? expiresAt) =>
        expiresAt is null || expiresAt <= _clock().AddSeconds(60);

    private static DateTimeOffset? ReadExpiry(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String &&
            DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out var stringDate))
        {
            return stringDate;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number))
        {
            return null;
        }

        return number > 10_000_000_000
            ? DateTimeOffset.FromUnixTimeMilliseconds((long)number)
            : DateTimeOffset.FromUnixTimeSeconds((long)number);
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static TimeSpan? ReadRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            ? TimeSpan.FromSeconds(Math.Clamp(seconds, 0, 3600))
            : null;
    }

    private sealed record Credential(
        string AccessToken,
        DateTimeOffset? ExpiresAt,
        string? SubscriptionType,
        string? RateLimitTier);
}
