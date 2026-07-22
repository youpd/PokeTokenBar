using System.Globalization;
using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Usage;

public sealed class LocalClaudeProvider : IUsageProvider
{
    private readonly LocalUsageCache _cache;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly CultureInfo _culture;

    public LocalClaudeProvider(
        LocalUsageCache cache,
        Func<DateTimeOffset>? clock = null,
        TimeZoneInfo? timeZone = null,
        CultureInfo? culture = null)
    {
        _cache = cache;
        _clock = clock ?? (() => DateTimeOffset.Now);
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _culture = culture ?? CultureInfo.CurrentCulture;
    }

    public string Id => "claude_code";

    public string DisplayName => "Claude Code";

    public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken)
    {
        var now = _clock();
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entries = _cache.ClaudeEntries(
                    LocalUsageReader.StartOfDay(now, _timeZone),
                    _timeZone);
                cancellationToken.ThrowIfCancellationRequested();
                return LocalUsageReader.Daily(
                    entries,
                    LocalUsageReader.TodayKey(now, _timeZone));
            },
            cancellationToken);
    }

    public Task<ProviderEnrichment> FetchEnrichmentAsync(
        CancellationToken cancellationToken)
    {
        var now = _clock();
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var monthStart = LocalUsageReader.StartOfMonth(now, _timeZone);
                var weekStart = LocalUsageReader.StartOfWeek(now, _culture, _timeZone);
                var scanStart = monthStart <= weekStart ? monthStart : weekStart;
                var entries = _cache.ClaudeEntries(scanStart, _timeZone);
                cancellationToken.ThrowIfCancellationRequested();

                return LocalProviderAggregation.Enrichment(
                    entries,
                    now,
                    weekStart,
                    monthStart,
                    _timeZone);
            },
            cancellationToken);
    }
}
