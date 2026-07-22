using System.Globalization;
using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Usage;

public sealed class LocalGeminiProvider : IUsageProvider
{
    private readonly LocalUsageCache _cache;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly CultureInfo _culture;

    public LocalGeminiProvider(
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

    public string Id => "gemini";

    public string DisplayName => "Gemini";

    public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken)
    {
        var now = _clock();
        return Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var entries = _cache.GeminiEntries(
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
                var entries = _cache.GeminiEntries(scanStart, _timeZone);
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
