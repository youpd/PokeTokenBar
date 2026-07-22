using System.Globalization;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Tests;

public sealed class LocalProviderTests
{
    [Fact]
    public async Task CodexProviderForcesSubscriptionCostToZero()
    {
        using var temporary = new TemporaryDirectory();
        var root = System.IO.Path.Combine(temporary.Path, "sessions");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            System.IO.Path.Combine(root, "rollout-test.jsonl"),
            "{\"timestamp\":\"2026-07-22T10:00:00Z\",\"payload\":{\"type\":\"token_count\"," +
            "\"info\":{\"last_token_usage\":{\"input_tokens\":100," +
            "\"cached_input_tokens\":80,\"output_tokens\":7}}}}");
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var cache = new LocalUsageCache(
            [],
            [root],
            [],
            System.IO.Path.Combine(temporary.Path, "usage-cache.json"),
            () => now);
        var provider = new LocalCodexProvider(
            cache,
            () => now,
            TimeZoneInfo.Utc,
            CultureInfo.InvariantCulture);

        var daily = await provider.FetchDailyAsync(TestContext.Current.CancellationToken);
        var enrichment = await provider.FetchEnrichmentAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(daily);
        Assert.Equal(107, daily.TotalTokens);
        Assert.Equal(20, daily.InputTokens);
        Assert.Equal(80, daily.CacheReadTokens);
        Assert.Equal(0, daily.TotalCost);
        Assert.Equal(0, enrichment.WeekTotal?.TotalCost);
        Assert.Equal(0, enrichment.MonthTotal?.TotalCost);
    }
}
