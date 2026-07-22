using System.Text.Json;
using System.Text.Json.Serialization;

namespace PokeTokenBar.Core.Util;

public sealed record AvailableUpdate(string Version, Uri PageUri);

public sealed class UpdateChecker : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly bool _ownsClient;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Uri _endpoint;
    private DateTimeOffset? _lastChecked;

    public UpdateChecker(
        string currentVersion,
        HttpClient? httpClient = null,
        Func<DateTimeOffset>? clock = null,
        Uri? endpoint = null)
    {
        CurrentVersion = currentVersion.Split('+')[0];
        _ownsClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _endpoint = endpoint ?? new Uri(
            "https://api.github.com/repos/chattymin/PokeTokenBar/releases?per_page=20");
    }

    public string CurrentVersion { get; }

    public AvailableUpdate? Available { get; private set; }

    public string? LastError { get; private set; }

    public event EventHandler? Changed;

    public async Task<AvailableUpdate?> CheckAsync(
        string? skippedVersion = null,
        TimeSpan? minimumInterval = null,
        CancellationToken cancellationToken = default)
    {
        var interval = minimumInterval ?? TimeSpan.FromMinutes(30);
        if (_lastChecked is { } last && _clock() - last < interval)
        {
            return Available;
        }

        _lastChecked = _clock();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.UserAgent.ParseAdd("PokeTokenBar");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            using var response = await _httpClient.SendAsync(request, cancellationToken)
                .ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync<List<ReleaseDto>>(
                    stream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false) ?? [];
            var latest = releases
                .Where(release => !release.Draft && !release.Prerelease &&
                    release.TagName?.StartsWith("win-v", StringComparison.OrdinalIgnoreCase) == true)
                .Select(release => new
                {
                    Release = release,
                    Version = release.TagName[5..],
                })
                .Select(value => new
                {
                    value.Release,
                    value.Version,
                    Parsed = TryParseVersion(value.Version, out var parsed) ? parsed : null,
                })
                .Where(value => value.Parsed is not null &&
                    IsValidGitHubPage(value.Release.HtmlUrl))
                .OrderByDescending(value => value.Parsed)
                .FirstOrDefault();
            Available = latest is not null &&
                IsNewer(latest.Version, CurrentVersion) &&
                !string.Equals(latest.Version, skippedVersion, StringComparison.OrdinalIgnoreCase)
                ? new AvailableUpdate(latest.Version, new Uri(latest.Release.HtmlUrl))
                : null;
            LastError = null;
        }
        catch (Exception exception) when (
            exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LastError = exception.Message;
            AppLog.Write($"update check failed: {exception.Message}");
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return Available;
    }

    public void ClearAvailable()
    {
        Available = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public static bool IsNewer(string candidate, string current) =>
        TryParseVersion(candidate, out var candidateVersion) &&
        TryParseVersion(current, out var currentVersion) &&
        candidateVersion > currentVersion;

    public static bool IsValidGitHubPage(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (_ownsClient) _httpClient.Dispose();
    }

    private static bool TryParseVersion(string value, out Version version)
    {
        var clean = value.Split('-', '+')[0];
        var parts = clean.Split('.');
        version = new Version(0, 0, 0, 0);
        if (parts.Length is < 2 or > 4 ||
            parts.Any(part => part.Length == 0 || !part.All(char.IsDigit)))
        {
            return false;
        }

        var numbers = new int[4];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out numbers[index])) return false;
        }

        version = new Version(numbers[0], numbers[1], numbers[2], numbers[3]);
        return true;
    }

    private sealed record ReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease);
}
