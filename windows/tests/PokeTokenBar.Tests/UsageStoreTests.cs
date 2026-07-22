using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Tests;

public sealed class UsageStoreTests
{
    public static TheoryData<bool, bool, bool, string?, string[]> TooltipCases =>
        new()
        {
            { false, false, false, null, [] },
            { true, false, false, null, ["1.2M"] },
            { false, true, false, null, ["$3.5"] },
            { false, false, true, null, [] },
            { true, true, false, null, ["1.2M", "$3.5"] },
            { true, false, true, null, ["1.2M"] },
            { false, true, true, null, ["$3.5"] },
            { true, true, true, null, ["1.2M", "$3.5"] },
            { false, false, false, "Claude 40%", [] },
            { true, false, false, "Claude 40%", ["1.2M"] },
            { false, true, false, "Claude 40%", ["$3.5"] },
            { false, false, true, "Claude 40%", ["Claude 40%"] },
            { true, true, false, "Claude 40%", ["1.2M", "$3.5"] },
            { true, false, true, "Claude 40%", ["1.2M", "Claude 40%"] },
            { false, true, true, "Claude 40%", ["$3.5", "Claude 40%"] },
            { true, true, true, "Claude 40%", ["1.2M", "$3.5", "Claude 40%"] },
        };

    [Theory]
    [MemberData(nameof(TooltipCases))]
    public void TooltipLinesAllCombinations(
        bool showTokens,
        bool showCost,
        bool showLimit,
        string? limitLine,
        string[] expected)
    {
        var lines = TrayText.UsageLines(
            hasUpdated: true,
            showTokens,
            showCost,
            showLimit,
            todayTokens: 1_200_000,
            todayCost: 3.45,
            limitLine);

        Assert.Equal(expected, lines);
    }

    [Fact]
    public void TooltipShowsPlaceholderBeforeFirstRefresh()
    {
        Assert.Equal(
            ["—"],
            TrayText.UsageLines(false, false, false, false, 0, 0, null));
    }

    [Fact]
    public async Task RefreshAggregatesDailyWeekMonthAndBlock()
    {
        var provider = new FakeProvider
        {
            Daily = TodayDaily(42_000_000, 12.5),
            Enrichment = Enrichment(200_000, 90_000_000, 300_000_000),
        };
        using var store = new UsageStore([provider]);

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(42_000_000, store.TodayTotalTokens);
        Assert.Equal(12.5, store.TodayCostTotal);
        Assert.Equal(90_000_000, store.WeekTotalTokens);
        Assert.Equal(300_000_000, store.MonthTotalTokens);
        Assert.Equal(BurnTier.Fast, store.BurnTier);
        Assert.NotNull(store.LastUpdated);
        Assert.Null(store.LastErrorDescription);
    }

    [Fact]
    public async Task ProviderFailureKeepsPreviousTodayValue()
    {
        var provider = new FakeProvider { Daily = TodayDaily(100_000_000) };
        using var store = new UsageStore([provider]);
        await store.RefreshAsync(false, TestContext.Current.CancellationToken);
        provider.FailDaily = true;

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(100_000_000, store.TodayTotalTokens);
        Assert.NotNull(store.LastErrorDescription);
    }

    [Fact]
    public async Task EnrichmentFailureKeepsPreviousBlockAndPeriods()
    {
        var provider = new FakeProvider
        {
            Daily = TodayDaily(1_000),
            Enrichment = Enrichment(200_000, 7_000, 30_000),
        };
        using var store = new UsageStore([provider]);
        await store.RefreshAsync(false, TestContext.Current.CancellationToken);
        provider.FailEnrichment = true;

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(7_000, store.WeekTotalTokens);
        Assert.Equal(30_000, store.MonthTotalTokens);
        Assert.Equal(BurnTier.Fast, store.BurnTier);
    }

    [Fact]
    public async Task StaleDatedSnapshotIsExcludedFromTodayTotal()
    {
        var provider = new FakeProvider
        {
            Daily = new DailyUsage("2000-01-01", 999, 0, 0, 0, 999, 0),
        };
        using var store = new UsageStore([provider]);

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(0, store.TodayTotalTokens);
    }

    [Fact]
    public async Task MidnightDoesNotCarryPreviousDayAfterProviderFailure()
    {
        var now = new DateTimeOffset(2026, 7, 22, 23, 59, 0, TimeSpan.Zero);
        var provider = new FakeProvider { Daily = TodayDaily(99, "2026-07-22") };
        using var store = new UsageStore(
            [provider],
            () => now,
            TimeZoneInfo.Utc);
        await store.RefreshAsync(false, TestContext.Current.CancellationToken);
        now = now.AddMinutes(2);
        provider.FailDaily = true;

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(0, store.TodayTotalTokens);
        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public async Task MidnightCarrierSnapshotRequiresActiveBlock()
    {
        var provider = new FakeProvider
        {
            Daily = null,
            Enrichment = Enrichment(200_000, 90_000_000, 300_000_000),
        };
        using var store = new UsageStore([provider]);

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);

        var snapshot = Assert.Single(store.Snapshots);
        Assert.Null(snapshot.Today);
        Assert.NotNull(snapshot.ActiveBlock);
        Assert.Equal(90_000_000, store.WeekTotalTokens);
    }

    [Fact]
    public async Task WeekMonthOnlyDoesNotCreateCarrierSnapshot()
    {
        var provider = new FakeProvider
        {
            Daily = null,
            Enrichment = new ProviderEnrichment(
                null,
                BlocksOk: true,
                new PeriodUsage("week", 90, 0),
                new PeriodUsage("month", 300, 0),
                PeriodsOk: true),
        };
        using var store = new UsageStore([provider]);

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);

        Assert.Empty(store.Snapshots);
    }

    [Fact]
    public async Task PeriodsPersistWhenNextEnrichmentSkipsThem()
    {
        var provider = new FakeProvider
        {
            Daily = TodayDaily(1_000),
            Enrichment = Enrichment(0, 7_000, 30_000, includeBlock: false),
        };
        using var store = new UsageStore([provider]);
        await store.RefreshAsync(false, TestContext.Current.CancellationToken);
        provider.Enrichment = new ProviderEnrichment(PeriodsOk: false, BlocksOk: false);

        await store.RefreshAsync(false, TestContext.Current.CancellationToken);

        Assert.Equal(7_000, store.WeekTotalTokens);
        Assert.Equal(30_000, store.MonthTotalTokens);
    }

    [Fact]
    public void TokenFormatterMatchesDisplayContract()
    {
        Assert.Equal("987", TokenFormatter.Compact(987));
        Assert.Equal("12.3K", TokenFormatter.Compact(12_345));
        Assert.Equal("190.6M", TokenFormatter.Compact(190_612_940));
        Assert.Equal("1.24B", TokenFormatter.Compact(1_240_000_000));
        Assert.Equal("253,412,890", TokenFormatter.Grouped(253_412_890));
        Assert.Equal("$48.10", TokenFormatter.Cost(48.104));
        Assert.Equal("$9.5", TokenFormatter.CostCompact(9.54));
        Assert.Equal("$311", TokenFormatter.CostCompact(311.4));
        Assert.Equal("$12.3K", TokenFormatter.CostCompact(12_340));
        Assert.Equal("88%", TokenFormatter.Percent(88));
        Assert.Equal("88.3%", TokenFormatter.Percent(88.35));
    }

    private static DailyUsage TodayDaily(long tokens, double cost = 0) =>
        TodayDaily(tokens, LocalUsageReader.TodayKey(), cost);

    private static DailyUsage TodayDaily(long tokens, string date, double cost = 0) =>
        new(date, tokens, 0, 0, 0, tokens, cost);

    private static ProviderEnrichment Enrichment(
        double tokensPerMinute,
        long week,
        long month,
        bool includeBlock = true)
    {
        var now = DateTimeOffset.Now;
        return new ProviderEnrichment(
            includeBlock
                ? new BlockUsage(
                    "block",
                    now.AddMinutes(-30),
                    now.AddHours(4.5),
                    true,
                    1_000,
                    0,
                    tokensPerMinute)
                : null,
            BlocksOk: true,
            new PeriodUsage("week", week, 0),
            new PeriodUsage("month", month, 0),
            PeriodsOk: true);
    }

    private sealed class FakeProvider : IUsageProvider
    {
        public string Id { get; init; } = "claude_code";

        public string DisplayName { get; init; } = "Claude Code";

        public DailyUsage? Daily { get; set; }

        public ProviderEnrichment Enrichment { get; set; } = new();

        public bool FailDaily { get; set; }

        public bool FailEnrichment { get; set; }

        public Task<DailyUsage?> FetchDailyAsync(CancellationToken cancellationToken) =>
            FailDaily
                ? Task.FromException<DailyUsage?>(new InvalidOperationException("daily failed"))
                : Task.FromResult(Daily);

        public Task<ProviderEnrichment> FetchEnrichmentAsync(
            CancellationToken cancellationToken) =>
            FailEnrichment
                ? Task.FromException<ProviderEnrichment>(
                    new InvalidOperationException("enrichment failed"))
                : Task.FromResult(Enrichment);
    }
}
