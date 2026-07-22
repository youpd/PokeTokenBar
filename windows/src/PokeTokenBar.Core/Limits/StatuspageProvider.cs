using System.Text.Json;
using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Limits;

public sealed class StatuspageProvider : IProviderStatusProvider, IDisposable
{
    private static readonly IReadOnlyDictionary<string, Uri> DefaultEndpoints =
        new Dictionary<string, Uri>(StringComparer.Ordinal)
        {
            ["claude_code"] = new("https://status.anthropic.com/api/v2/status.json"),
            ["codex"] = new("https://status.openai.com/api/v2/status.json"),
        };

    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly IReadOnlyDictionary<string, Uri> _endpoints;

    public StatuspageProvider(
        HttpClient? httpClient = null,
        IReadOnlyDictionary<string, Uri>? endpoints = null)
    {
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        _endpoints = endpoints ?? DefaultEndpoints;
    }

    public async Task<IReadOnlyDictionary<string, ProviderStatus>> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        var results = await Task.WhenAll(_endpoints.Select(async pair =>
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, pair.Value);
                request.Headers.UserAgent.ParseAdd("PokeTokenBar");
                using var response = await _httpClient.SendAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using var json = await JsonDocument.ParseAsync(
                        stream,
                        cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                var status = json.RootElement.GetProperty("status");
                var indicatorText = status.GetProperty("indicator").GetString();
                var description = status.GetProperty("description").GetString() ?? string.Empty;
                return new KeyValuePair<string, ProviderStatus>?(
                    new KeyValuePair<string, ProviderStatus>(
                        pair.Key,
                        new ProviderStatus(
                            pair.Key,
                            ParseIndicator(indicatorText),
                            description)));
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException ||
                !cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        })).ConfigureAwait(false);

        return results
            .Where(result => result.HasValue)
            .Select(result => result!.Value)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _httpClient.Dispose();
        }
    }

    private static ProviderStatusIndicator ParseIndicator(string? indicator) =>
        indicator?.ToLowerInvariant() switch
        {
            "none" => ProviderStatusIndicator.None,
            "minor" => ProviderStatusIndicator.Minor,
            "major" => ProviderStatusIndicator.Major,
            "critical" => ProviderStatusIndicator.Critical,
            "maintenance" => ProviderStatusIndicator.Maintenance,
            _ => ProviderStatusIndicator.Unknown,
        };
}
