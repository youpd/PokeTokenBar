using System.Text.Json;
using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Limits;

public sealed class StatuspageProvider : IProviderStatusProvider, IDisposable
{
    private static readonly IReadOnlyDictionary<string, Uri> DefaultEndpoints =
        new Dictionary<string, Uri>(StringComparer.Ordinal)
        {
            ["claude_code"] = new("https://status.anthropic.com/api/v2/status.json"),
            ["codex"] = new("https://status.openai.com/api/v2/components.json"),
        };

    private static readonly HashSet<string> CodexComponents = new(StringComparer.OrdinalIgnoreCase)
    {
        "Codex API",
        "Codex Web",
        "Codex in ChatGPT Desktop",
        "CLI",
        "VS Code extension",
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
                var status = pair.Key == "codex"
                    ? ParseCodexComponents(json.RootElement)
                    : ParseGlobalStatus(pair.Key, json.RootElement);
                return status is null
                    ? null
                    : new KeyValuePair<string, ProviderStatus>?(
                        new KeyValuePair<string, ProviderStatus>(pair.Key, status));
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

    private static ProviderStatus ParseGlobalStatus(string providerId, JsonElement root)
    {
        var status = root.GetProperty("status");
        var indicatorText = status.GetProperty("indicator").GetString();
        var description = status.GetProperty("description").GetString() ?? string.Empty;
        return new ProviderStatus(providerId, ParseIndicator(indicatorText), description);
    }

    private static ProviderStatus? ParseCodexComponents(JsonElement root)
    {
        if (!root.TryGetProperty("components", out var components) ||
            components.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var relevant = components.EnumerateArray()
            .Select(component => new
            {
                Name = component.TryGetProperty("name", out var name)
                    ? name.GetString()
                    : null,
                Status = component.TryGetProperty("status", out var status)
                    ? status.GetString()
                    : null,
            })
            .Where(component => component.Name is not null && CodexComponents.Contains(component.Name))
            .Select(component => new
            {
                Name = component.Name!,
                RawStatus = component.Status,
                Indicator = ParseComponentStatus(component.Status),
            })
            .ToList();
        if (relevant.Count == 0)
        {
            return null;
        }

        var incidentComponents = relevant
            .Where(component => component.Indicator is not ProviderStatusIndicator.None)
            .ToList();
        if (incidentComponents.Count == 0)
        {
            return new ProviderStatus("codex", ProviderStatusIndicator.None, "Operational");
        }

        var indicator = incidentComponents
            .OrderByDescending(component => Severity(component.Indicator))
            .First()
            .Indicator;
        var description = string.Join(
            ", ",
            incidentComponents.Select(component =>
                $"{component.Name}: {Humanize(component.RawStatus)}"));
        return new ProviderStatus("codex", indicator, description);
    }

    private static ProviderStatusIndicator ParseComponentStatus(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "operational" => ProviderStatusIndicator.None,
            "degraded_performance" => ProviderStatusIndicator.Minor,
            "partial_outage" => ProviderStatusIndicator.Major,
            "major_outage" => ProviderStatusIndicator.Critical,
            "under_maintenance" => ProviderStatusIndicator.Maintenance,
            _ => ProviderStatusIndicator.Unknown,
        };

    private static int Severity(ProviderStatusIndicator indicator) => indicator switch
    {
        ProviderStatusIndicator.Critical => 5,
        ProviderStatusIndicator.Major => 4,
        ProviderStatusIndicator.Minor => 3,
        ProviderStatusIndicator.Maintenance => 2,
        ProviderStatusIndicator.Unknown => 1,
        _ => 0,
    };

    private static string Humanize(string? status) => string.IsNullOrWhiteSpace(status)
        ? "unknown"
        : status.Replace('_', ' ');

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
