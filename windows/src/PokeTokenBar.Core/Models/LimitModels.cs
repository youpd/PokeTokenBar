using System.Globalization;
using System.Text.Json.Serialization;

namespace PokeTokenBar.Core.Models;

public sealed record LimitWindow(
    [property: JsonPropertyName("utilization")] double? Utilization,
    [property: JsonPropertyName("resets_at")] string? ResetsAt)
{
    public DateTimeOffset? ResetDate => DateTimeOffset.TryParse(
        ResetsAt,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AssumeUniversal,
        out var value)
        ? value
        : null;
}

public sealed record OAuthLimitModel(
    [property: JsonPropertyName("display_name")] string? DisplayName);

public sealed record OAuthLimitScope(
    [property: JsonPropertyName("model")] OAuthLimitModel? Model);

public sealed record OAuthLimitEntry(
    [property: JsonPropertyName("kind")] string? Kind,
    [property: JsonPropertyName("group")] string? Group,
    [property: JsonPropertyName("percent")] double? Percent,
    [property: JsonPropertyName("severity")] string? Severity,
    [property: JsonPropertyName("resets_at")] string? ResetsAt,
    [property: JsonPropertyName("scope")] OAuthLimitScope? Scope,
    [property: JsonPropertyName("is_active")] bool? IsActive)
{
    public string DisplayName => Scope?.Model?.DisplayName
        ?? Group
        ?? Kind?.Replace('_', ' ')
        ?? "Limit";
}

public sealed record ClaudeLimitStatus(
    [property: JsonPropertyName("five_hour")] LimitWindow? FiveHour,
    [property: JsonPropertyName("seven_day")] LimitWindow? SevenDay,
    [property: JsonPropertyName("seven_day_opus")] LimitWindow? SevenDayOpus,
    [property: JsonPropertyName("seven_day_sonnet")] LimitWindow? SevenDaySonnet,
    [property: JsonPropertyName("limits")] IReadOnlyList<OAuthLimitEntry>? Limits)
{
    [JsonIgnore]
    public string? SubscriptionType { get; init; }

    [JsonIgnore]
    public string? RateLimitTier { get; init; }

    [JsonIgnore]
    public string? PlanDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(SubscriptionType))
            {
                return null;
            }

            var plan = char.ToUpperInvariant(SubscriptionType[0]) + SubscriptionType[1..];
            var multiplier = (RateLimitTier ?? string.Empty)
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(part => part.EndsWith('x') &&
                    int.TryParse(part[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out _));
            return multiplier is null ? plan : $"{plan} {multiplier}";
        }
    }

    [JsonIgnore]
    public IReadOnlyList<OAuthLimitEntry> ScopedLimitEntries
    {
        get
        {
            var entries = Limits ?? [];
            if (FiveHour is null && SevenDay is null)
            {
                return entries;
            }

            return entries
                .Where(entry => entry.Kind is not "session" and not "weekly_all")
                .ToArray();
        }
    }
}

public sealed record CodexRateLimitWindow(
    [property: JsonPropertyName("usedPercent")] double UsedPercent,
    [property: JsonPropertyName("windowDurationMins")] int? WindowDurationMins,
    [property: JsonPropertyName("resetsAt")] long? ResetsAt)
{
    public DateTimeOffset? ResetDate => ResetsAt is { } value
        ? DateTimeOffset.FromUnixTimeSeconds(value)
        : null;
}

public sealed record CodexCredits(
    [property: JsonPropertyName("balance")] string? Balance,
    [property: JsonPropertyName("hasCredits")] bool HasCredits,
    [property: JsonPropertyName("unlimited")] bool Unlimited);

public sealed record CodexIndividualLimit(
    [property: JsonPropertyName("limit")] double Limit,
    [property: JsonPropertyName("remainingPercent")] double RemainingPercent,
    [property: JsonPropertyName("resetsAt")] long? ResetsAt,
    [property: JsonPropertyName("used")] double Used)
{
    public double UsedPercent => Math.Clamp(100 - RemainingPercent, 0, 100);

    public DateTimeOffset? ResetDate => ResetsAt is { } value
        ? DateTimeOffset.FromUnixTimeSeconds(value)
        : null;
}

public sealed record CodexRateLimitSnapshot(
    [property: JsonPropertyName("limitId")] string? LimitId,
    [property: JsonPropertyName("limitName")] string? LimitName,
    [property: JsonPropertyName("primary")] CodexRateLimitWindow? Primary,
    [property: JsonPropertyName("secondary")] CodexRateLimitWindow? Secondary,
    [property: JsonPropertyName("credits")] CodexCredits? Credits,
    [property: JsonPropertyName("individualLimit")] CodexIndividualLimit? IndividualLimit,
    [property: JsonPropertyName("planType")] string? PlanType,
    [property: JsonPropertyName("rateLimitReachedType")] string? RateLimitReachedType)
{
    public string BucketKey => LimitId ?? LimitName ?? "codex";

    public string DisplayName => LimitName ?? (LimitId is null or "codex" ? "Codex" : LimitId);

    public bool IsVisible => Primary is not null || Secondary is not null || IndividualLimit is not null;
}

public sealed record CodexRateLimitsResult(
    [property: JsonPropertyName("rateLimits")] CodexRateLimitSnapshot? RateLimits,
    [property: JsonPropertyName("rateLimitsByLimitId")] IReadOnlyDictionary<string, CodexRateLimitSnapshot>? RateLimitsByLimitId)
{
    public IReadOnlyList<CodexRateLimitSnapshot> Snapshots
    {
        get
        {
            var values = new List<CodexRateLimitSnapshot>();
            var primaryKey = RateLimits?.LimitId ?? "codex";
            if (RateLimits is not null)
            {
                values.Add(RateLimits);
            }

            if (RateLimitsByLimitId is not null)
            {
                values.AddRange(RateLimitsByLimitId
                    .OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .Where(pair => !string.Equals(
                        pair.Value.LimitId ?? pair.Key,
                        primaryKey,
                        StringComparison.Ordinal))
                    .Select(pair => pair.Value));
            }

            return values;
        }
    }

    public double? MaxPrimaryUsedPercent => Snapshots
        .Where(snapshot => snapshot.IsVisible && snapshot.Primary is not null)
        .Select(snapshot => (double?)snapshot.Primary!.UsedPercent)
        .Max();
}

public enum ProviderStatusIndicator
{
    None,
    Minor,
    Major,
    Critical,
    Maintenance,
    Unknown,
}

public sealed record ProviderStatus(
    string ProviderId,
    ProviderStatusIndicator Indicator,
    string Description)
{
    public bool IsIncident => Indicator is not ProviderStatusIndicator.None;
}

public sealed record FiveHourForecast(
    DateTimeOffset DepletionDate,
    bool BeforeReset);

public sealed record LimitWindowReading(
    string Key,
    string Name,
    double Utilization);

public sealed record LimitAlert(
    string Key,
    string Name,
    bool IsCritical,
    double Utilization);
