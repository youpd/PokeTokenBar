using System.Globalization;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Tests;

public sealed class LocalProviderTests
{
    [Fact]
    public async Task CodexProviderReturnsApiEquivalentSubscriptionCost()
    {
        using var temporary = new TemporaryDirectory();
        var root = System.IO.Path.Combine(temporary.Path, "sessions");
        Directory.CreateDirectory(root);
        File.WriteAllText(
            System.IO.Path.Combine(root, "rollout-test.jsonl"),
            "{\"timestamp\":\"2026-07-22T09:59:00Z\",\"payload\":{\"type\":\"turn_context\"," +
            "\"model\":\"gpt-5.6-sol\"}}" + Environment.NewLine +
            "{\"timestamp\":\"2026-07-22T10:00:00Z\",\"payload\":{\"type\":\"token_count\"," +
            "\"info\":{\"last_token_usage\":{\"input_tokens\":1000000," +
            "\"cached_input_tokens\":800000,\"output_tokens\":100000}}}}");
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
        Assert.Equal(1_100_000, daily.TotalTokens);
        Assert.Equal(200_000, daily.InputTokens);
        Assert.Equal(800_000, daily.CacheReadTokens);
        Assert.Equal(4.4, daily.TotalCost, precision: 6);
        Assert.NotNull(enrichment.WeekTotal);
        Assert.NotNull(enrichment.MonthTotal);
        Assert.Equal(4.4, enrichment.WeekTotal!.TotalCost, precision: 6);
        Assert.Equal(4.4, enrichment.MonthTotal!.TotalCost, precision: 6);
    }
}
