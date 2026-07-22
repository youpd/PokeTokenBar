using System.Diagnostics;
using System.Text.Json;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Core.Usage;

public sealed class UsageStore : IDisposable
{
    private static readonly JsonSerializerOptions SnapshotJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IReadOnlyList<IUsageProvider> _providers;
    private readonly Func<DateTimeOffset> _clock;
    private readonly TimeZoneInfo _timeZone;
    private readonly string? _snapshotFile;
    private IReadOnlyList<ProviderSnapshot> _snapshots = [];
    private CancellationTokenSource? _emptyRetryCancellation;
    private int _refreshing;
    private bool _disposed;

    public UsageStore(
        IEnumerable<IUsageProvider> providers,
        Func<DateTimeOffset>? clock = null,
        TimeZoneInfo? timeZone = null,
        string? snapshotFile = null)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers.ToArray();
        var duplicate = _providers
            .GroupBy(provider => provider.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Duplicate usage provider id: {duplicate.Key}",
                nameof(providers));
        }

        _clock = clock ?? (() => DateTimeOffset.Now);
        _timeZone = timeZone ?? TimeZoneInfo.Local;
        _snapshotFile = snapshotFile;
    }

    public event EventHandler? Changed;

    public IReadOnlyList<ProviderSnapshot> Snapshots => _snapshots;

    public IReadOnlyList<string> RegisteredProviderIds =>
        _providers.Select(provider => provider.Id).ToArray();

    public DateTimeOffset? LastUpdated { get; private set; }

    public string? LastErrorDescription { get; private set; }

    public bool IsRefreshing => Volatile.Read(ref _refreshing) != 0;

    public bool HasUsageData => _snapshots.Count != 0;

    public long TodayTotalTokens
    {
        get
        {
            var today = TodayKey;
            return _snapshots.Sum(snapshot =>
                snapshot.Today?.Date == today ? snapshot.Today.TotalTokens : 0);
        }
    }

    public double TodayCostTotal
    {
        get
        {
            var today = TodayKey;
            return _snapshots.Sum(snapshot =>
                snapshot.Today?.Date == today ? snapshot.Today.TotalCost : 0);
        }
    }

    public long WeekTotalTokens =>
        _snapshots.Sum(snapshot => snapshot.WeekTotal?.TotalTokens ?? 0);

    public double WeekCostTotal =>
        _snapshots.Sum(snapshot => snapshot.WeekTotal?.TotalCost ?? 0);

    public long MonthTotalTokens =>
        _snapshots.Sum(snapshot => snapshot.MonthTotal?.TotalTokens ?? 0);

    public double MonthCostTotal =>
        _snapshots.Sum(snapshot => snapshot.MonthTotal?.TotalCost ?? 0);

    public double CombinedBurnPerMinute =>
        _snapshots.Sum(snapshot => snapshot.ActiveBlock?.TokensPerMinute ?? 0);

    public BurnTier BurnTier => CombinedBurnPerMinute switch
    {
        <= 1_000 => BurnTier.Idle,
        < 100_000 => BurnTier.Normal,
        < 400_000 => BurnTier.Fast,
        _ => BurnTier.Blazing,
    };

    public BlockUsage? ClaudeActiveBlock =>
        _snapshots.FirstOrDefault(snapshot => snapshot.ProviderId == "claude_code")?
            .ActiveBlock;

    public string TodayKey => LocalUsageReader.TodayKey(_clock(), _timeZone);

    public bool IsStale(TimeSpan refreshInterval)
    {
        if (LastUpdated is not { } updated)
        {
            return true;
        }

        var allowance = refreshInterval > TimeSpan.Zero
            ? refreshInterval * 2
            : TimeSpan.FromMinutes(30);
        return _clock() - updated > allowance;
    }

    public ProviderSnapshot? SnapshotPreferring(string? providerId)
    {
        if (!string.IsNullOrEmpty(providerId))
        {
            var preferred = _snapshots.FirstOrDefault(snapshot =>
                snapshot.ProviderId == providerId);
            if (preferred is not null)
            {
                return preferred;
            }
        }

        return _snapshots.FirstOrDefault();
    }

    public async Task RefreshAsync(
        bool scheduleEmptyRetry = true,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var startedAt = Stopwatch.GetTimestamp();
            CancelEmptyRetry();
            var now = _clock();
            var todayKey = LocalUsageReader.TodayKey(now, _timeZone);
            var previousById = _snapshots.ToDictionary(
                snapshot => snapshot.ProviderId,
                StringComparer.Ordinal);

            var dailyOutcomes = await Task.WhenAll(_providers.Select(async provider =>
            {
                try
                {
                    var today = await provider.FetchDailyAsync(cancellationToken)
                        .ConfigureAwait(true);
                    return new DailyOutcome(provider, today, null);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested)
                {
                    return new DailyOutcome(provider, null, exception);
                }
            })).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            var newSnapshots = new List<ProviderSnapshot>();
            var errors = new List<string>();

            foreach (var outcome in dailyOutcomes)
            {
                previousById.TryGetValue(outcome.Provider.Id, out var previous);
                var previousToday = previous?.Today?.Date == todayKey
                    ? previous.Today
                    : null;
                DailyUsage? today;
                if (outcome.Error is null)
                {
                    today = outcome.Today;
                }
                else
                {
                    today = previousToday;
                    errors.Add($"{outcome.Provider.Id}: {outcome.Error.Message}");
                }

                var carryActiveBlock = previous?.ActiveBlock;
                if (today is not null || carryActiveBlock is not null)
                {
                    newSnapshots.Add(new ProviderSnapshot(
                        outcome.Provider.Id,
                        outcome.Provider.DisplayName,
                        today,
                        carryActiveBlock,
                        previous?.WeekTotal,
                        previous?.MonthTotal,
                        now));
                }
            }

            _snapshots = newSnapshots;
            if (errors.Count == 0)
            {
                LastUpdated = now;
                LastErrorDescription = null;
            }
            else
            {
                LastErrorDescription = string.Join(" / ", errors);
                if (LastUpdated is null && _snapshots.Count != 0)
                {
                    LastUpdated = now;
                }
            }

            var enrichmentOutcomes = await Task.WhenAll(_providers.Select(async provider =>
            {
                try
                {
                    var enrichment = await provider.FetchEnrichmentAsync(cancellationToken)
                        .ConfigureAwait(true);
                    return new EnrichmentOutcome(provider, enrichment, null);
                }
                catch (Exception exception) when (
                    exception is not OperationCanceledException ||
                    !cancellationToken.IsCancellationRequested)
                {
                    return new EnrichmentOutcome(provider, null, exception);
                }
            })).ConfigureAwait(true);

            cancellationToken.ThrowIfCancellationRequested();
            var enriched = _snapshots.ToList();
            foreach (var outcome in enrichmentOutcomes)
            {
                if (outcome.Error is not null || outcome.Enrichment is null)
                {
                    if (outcome.Error is not null)
                    {
                        AppLog.Write(
                            $"enrichment failed for {outcome.Provider.Id}: {outcome.Error.Message}");
                    }

                    continue;
                }

                var enrichment = outcome.Enrichment;
                var index = enriched.FindIndex(snapshot =>
                    snapshot.ProviderId == outcome.Provider.Id);
                if (index < 0)
                {
                    if (enrichment.BlocksOk && enrichment.ActiveBlock is not null)
                    {
                        enriched.Add(new ProviderSnapshot(
                            outcome.Provider.Id,
                            outcome.Provider.DisplayName,
                            null,
                            enrichment.ActiveBlock,
                            enrichment.PeriodsOk ? enrichment.WeekTotal : null,
                            enrichment.PeriodsOk ? enrichment.MonthTotal : null,
                            now));
                    }

                    continue;
                }

                var current = enriched[index];
                var updated = current with
                {
                    ActiveBlock = enrichment.BlocksOk
                        ? enrichment.ActiveBlock
                        : current.ActiveBlock,
                    WeekTotal = enrichment.PeriodsOk
                        ? enrichment.WeekTotal
                        : current.WeekTotal,
                    MonthTotal = enrichment.PeriodsOk
                        ? enrichment.MonthTotal
                        : current.MonthTotal,
                };
                if (updated.Today is null && updated.ActiveBlock is null)
                {
                    enriched.RemoveAt(index);
                }
                else
                {
                    enriched[index] = updated;
                }
            }

            _snapshots = enriched;
            WriteParitySnapshot();
            AppLog.Write(
                $"usage refresh complete: today={TodayTotalTokens}, providers={_snapshots.Count}, " +
                $"elapsedMs={Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds:F0}");

            if (scheduleEmptyRetry && errors.Count == 0 && _snapshots.Count == 0)
            {
                ScheduleEmptyRetry();
            }
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        CancelEmptyRetry();
    }

    private void ScheduleEmptyRetry()
    {
        var cancellation = new CancellationTokenSource();
        _emptyRetryCancellation = cancellation;
        _ = RetryEmptyUsageAsync(cancellation);
        AppLog.Write("empty usage retry scheduled");
    }

    private async Task RetryEmptyUsageAsync(CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(20), cancellation.Token)
                .ConfigureAwait(false);
            if (!cancellation.IsCancellationRequested && !_disposed)
            {
                await RefreshAsync(
                        scheduleEmptyRetry: false,
                        cancellation.Token)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // A normal refresh or shutdown superseded this one-shot retry.
        }
        finally
        {
            if (ReferenceEquals(_emptyRetryCancellation, cancellation))
            {
                _emptyRetryCancellation = null;
            }

            cancellation.Dispose();
        }
    }

    private void CancelEmptyRetry()
    {
        var cancellation = _emptyRetryCancellation;
        _emptyRetryCancellation = null;
        cancellation?.Cancel();
    }

    private void WriteParitySnapshot()
    {
        if (string.IsNullOrWhiteSpace(_snapshotFile))
        {
            return;
        }

        try
        {
            var snapshot = new
            {
                generatedAt = _clock(),
                todayKey = TodayKey,
                todayTotalTokens = TodayTotalTokens,
                todayCostTotal = TodayCostTotal,
                weekTotalTokens = WeekTotalTokens,
                monthTotalTokens = MonthTotalTokens,
                providers = _snapshots,
            };
            var json = JsonSerializer.Serialize(snapshot, SnapshotJsonOptions);
            var fullPath = Path.GetFullPath(_snapshotFile);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryFile = fullPath + ".tmp";
            File.WriteAllText(temporaryFile, json);
            File.Move(temporaryFile, fullPath, overwrite: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            AppLog.Write($"parity snapshot write failed: {exception.Message}");
        }
    }

    private sealed record DailyOutcome(
        IUsageProvider Provider,
        DailyUsage? Today,
        Exception? Error);

    private sealed record EnrichmentOutcome(
        IUsageProvider Provider,
        ProviderEnrichment? Enrichment,
        Exception? Error);
}
