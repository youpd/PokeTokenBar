namespace PokeTokenBar.Core.Models;

public sealed record UsageEntry(
    string Id,
    DateTimeOffset Date,
    string LocalDay,
    string Model,
    long Input,
    long Output,
    long CacheWrite,
    long CacheRead)
{
    public long Total => Input + Output + CacheWrite + CacheRead;
}

public sealed record DailyUsage(
    string Date,
    long InputTokens,
    long OutputTokens,
    long CacheCreationTokens,
    long CacheReadTokens,
    long TotalTokens,
    double TotalCost);

public sealed record PeriodUsage(
    string Period,
    long TotalTokens,
    double TotalCost);

public sealed record BlockUsage(
    string Id,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    bool IsActive,
    long TotalTokens,
    double CostUsd,
    double TokensPerMinute);

public sealed record ProviderEnrichment(
    BlockUsage? ActiveBlock = null,
    bool BlocksOk = false,
    PeriodUsage? WeekTotal = null,
    PeriodUsage? MonthTotal = null,
    bool PeriodsOk = false);

public sealed record ProviderSnapshot(
    string ProviderId,
    string DisplayName,
    DailyUsage? Today,
    BlockUsage? ActiveBlock,
    PeriodUsage? WeekTotal,
    PeriodUsage? MonthTotal,
    DateTimeOffset FetchedAt)
{
    public long TodayTotalTokens => Today?.TotalTokens ?? 0;
}

public enum BurnTier
{
    Idle,
    Normal,
    Fast,
    Blazing,
}
