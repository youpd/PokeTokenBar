using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Limits;

public static class LimitLogic
{
    public static IReadOnlyList<LimitAlert> EvaluateLimitAlerts(
        IEnumerable<LimitWindowReading> windows,
        int warn,
        int critical,
        IDictionary<string, int> tiers)
    {
        ArgumentNullException.ThrowIfNull(windows);
        ArgumentNullException.ThrowIfNull(tiers);
        var alerts = new List<LimitAlert>();
        foreach (var window in windows)
        {
            var tier = window.Utilization >= critical
                ? 2
                : window.Utilization >= warn
                    ? 1
                    : 0;
            if (tier == 0)
            {
                tiers.Remove(window.Key);
                continue;
            }

            tiers.TryGetValue(window.Key, out var previous);
            if (tier > previous)
            {
                alerts.Add(new LimitAlert(
                    window.Key,
                    window.Name,
                    tier == 2,
                    window.Utilization));
            }

            tiers[window.Key] = tier;
        }

        return alerts;
    }

    public static FiveHourForecast? Forecast(
        ClaudeLimitStatus? limits,
        BlockUsage? activeBlock,
        DateTimeOffset now)
    {
        var utilization = limits?.FiveHour?.Utilization;
        if (utilization is null || activeBlock is null)
        {
            return null;
        }

        if (utilization >= 100)
        {
            return new FiveHourForecast(now, true);
        }

        if (utilization < 5 || activeBlock.TotalTokens <= 0 ||
            activeBlock.TokensPerMinute < 10_000)
        {
            return null;
        }

        var tokensPerPercent = activeBlock.TotalTokens / utilization.Value;
        var minutesLeft = (100 - utilization.Value) * tokensPerPercent /
            activeBlock.TokensPerMinute;
        if (minutesLeft < 0 || minutesLeft >= TimeSpan.FromHours(24).TotalMinutes)
        {
            return null;
        }

        var depletion = now.AddMinutes(minutesLeft);
        return new FiveHourForecast(
            depletion,
            limits!.FiveHour!.ResetDate is { } reset && depletion < reset);
    }

    public static IReadOnlyList<LimitWindowReading> BuildReadings(
        ClaudeLimitStatus? claude,
        CodexRateLimitsResult? codex)
    {
        var values = new List<LimitWindowReading>();
        AddClaude(values, "claude.fiveHour", "Claude 5h", claude?.FiveHour);
        AddClaude(values, "claude.sevenDay", "Claude weekly", claude?.SevenDay);
        AddClaude(values, "claude.sevenDayOpus", "Claude Opus weekly", claude?.SevenDayOpus);
        AddClaude(values, "claude.sevenDaySonnet", "Claude Sonnet weekly", claude?.SevenDaySonnet);
        if (claude is not null)
        {
            for (var index = 0; index < claude.ScopedLimitEntries.Count; index++)
            {
                var entry = claude.ScopedLimitEntries[index];
                if (entry.Percent is { } percent)
                {
                    values.Add(new LimitWindowReading(
                        $"claude.scoped.{entry.Kind}.{entry.Scope?.Model?.DisplayName}.{index}",
                        entry.DisplayName,
                        percent));
                }
            }
        }

        foreach (var snapshot in codex?.Snapshots ?? [])
        {
            var key = snapshot.BucketKey;
            if (snapshot.Primary is { } primary)
            {
                values.Add(new LimitWindowReading(
                    $"codex.{key}.primary",
                    $"{snapshot.DisplayName} primary",
                    primary.UsedPercent));
            }

            if (snapshot.Secondary is { } secondary)
            {
                values.Add(new LimitWindowReading(
                    $"codex.{key}.secondary",
                    $"{snapshot.DisplayName} secondary",
                    secondary.UsedPercent));
            }

            if (snapshot.IndividualLimit is { } individual)
            {
                values.Add(new LimitWindowReading(
                    $"codex.{key}.individual",
                    $"{snapshot.DisplayName} spend",
                    individual.UsedPercent));
            }
        }

        return values;
    }

    private static void AddClaude(
        ICollection<LimitWindowReading> values,
        string key,
        string name,
        LimitWindow? window)
    {
        if (window?.Utilization is { } utilization)
        {
            values.Add(new LimitWindowReading(key, name, utilization));
        }
    }
}
