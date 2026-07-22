using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Usage;

internal static class LocalProviderAggregation
{
    public static ProviderEnrichment Enrichment(
        IReadOnlyList<UsageEntry> entries,
        DateTimeOffset now,
        DateTimeOffset weekStart,
        DateTimeOffset monthStart,
        TimeZoneInfo timeZone)
    {
        var today = LocalUsageReader.TodayKey(now, timeZone);
        var weekDay = LocalUsageReader.ToDayKey(weekStart, timeZone);
        var monthDay = LocalUsageReader.ToDayKey(monthStart, timeZone);
        var week = LocalUsageReader.Period(entries, weekDay, weekDay, today);
        var month = LocalUsageReader.Period(
            entries,
            LocalUsageReader.MonthKey(now, timeZone),
            monthDay,
            today);
        return new ProviderEnrichment(
            LocalUsageReader.ActiveBlock(entries, now),
            BlocksOk: true,
            week,
            month,
            PeriodsOk: true);
    }
}
