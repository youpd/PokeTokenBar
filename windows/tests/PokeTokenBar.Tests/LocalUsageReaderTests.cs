using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Tests;

public sealed class LocalUsageReaderTests
{
    [Theory]
    [InlineData("2026-07-22T02:11:05.034464+00:00")]
    [InlineData("2026-07-22T02:11:05.303Z")]
    [InlineData("2026-07-22T02:11:05Z")]
    public void IsoParserAcceptsSupportedTimestampShapes(string value)
    {
        Assert.True(LocalUsageReader.TryParseTimestamp(value, out var timestamp));
        Assert.Equal(2026, timestamp.UtcDateTime.Year);
        Assert.Equal(7, timestamp.UtcDateTime.Month);
        Assert.Equal(22, timestamp.UtcDateTime.Day);
    }

    [Fact]
    public void ClaudeParserSkipsMalformedLinesAndKeepsMaximumDuplicate()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "session.jsonl");
        File.WriteAllLines(path,
        [
            "not json assistant usage",
            "{\"type\":\"user\",\"assistant\":true,\"usage\":{}}",
            ClaudeLine("message", "request", 5),
            ClaudeLine("message", "request", 200),
            ClaudeLine("other", "request-2", 10, model: null),
        ]);

        var entries = LocalUsageReader.ParseClaudeFile(path, TimeZoneInfo.Utc);

        Assert.Equal(2, entries.Count);
        Assert.Equal(200, Assert.Single(entries, entry => entry.Id == "message|request").Output);
        Assert.Equal("unknown", Assert.Single(entries, entry => entry.Id == "other|request-2").Model);
    }

    [Fact]
    public void ClaudeCollectionDeduplicatesAcrossFilesAndScansRecursively()
    {
        using var temporary = new TemporaryDirectory();
        var nested = System.IO.Path.Combine(temporary.Path, "project", "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(
            System.IO.Path.Combine(temporary.Path, "first.jsonl"),
            ClaudeLine("message", "request", 10));
        File.WriteAllText(
            System.IO.Path.Combine(nested, "second.jsonl"),
            ClaudeLine("message", "request", 99));

        var entries = LocalUsageReader.ClaudeEntries(
            temporary.Path,
            DateTimeOffset.UnixEpoch,
            TimeZoneInfo.Utc);

        var entry = Assert.Single(entries);
        Assert.Equal(99, entry.Output);
    }

    [Fact]
    public void ClaudeParserCanReadFileWhileCliHoldsItOpen()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "live.jsonl");
        File.WriteAllText(path, ClaudeLine("message", "request", 42));
        using var writer = new FileStream(
            path,
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.ReadWrite);

        var entry = Assert.Single(LocalUsageReader.ParseClaudeFile(path, TimeZoneInfo.Utc));

        Assert.Equal(42, entry.Output);
    }

    [Fact]
    public void DailyPeriodAndActiveBlockAggregateTokensAndCost()
    {
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var entries = new[]
        {
            Entry("recent", now.AddMinutes(-30), input: 600_000),
            Entry("old", now.AddHours(-10), input: 400_000),
        };

        var daily = LocalUsageReader.Daily(entries, "2026-07-22");
        var period = LocalUsageReader.Period(
            entries,
            "2026-07",
            "2026-07-01",
            "2026-07-22");
        var block = LocalUsageReader.ActiveBlock(entries, now);

        Assert.NotNull(daily);
        Assert.Equal(1_000_000, daily.TotalTokens);
        Assert.Equal(5, daily.TotalCost, precision: 6);
        Assert.Equal(1_000_000, period.TotalTokens);
        Assert.NotNull(block);
        Assert.Equal(600_000, block.TotalTokens);
        Assert.Equal(20_000, block.TokensPerMinute, precision: 6);
    }

    [Fact]
    public void ModelPricingUsesExactFallbackAndUnknownRules()
    {
        Assert.Equal(5, ModelPricing.Cost("claude-opus-4-8", 1_000_000, 0, 0, 0));
        Assert.Equal(25, ModelPricing.Cost("claude-opus-4-8", 0, 1_000_000, 0, 0));
        Assert.Equal(5, ModelPricing.Cost("claude-opus-future", 1_000_000, 0, 0, 0));
        Assert.Equal(0, ModelPricing.Cost("claude-fable-5", 1_000_000, 1_000_000, 1_000_000, 1_000_000));
        Assert.Equal(0, ModelPricing.Cost("unknown", 1_000_000, 0, 0, 0));
    }

    [Fact]
    public void ModifiedSinceFiltersOldFiles()
    {
        using var temporary = new TemporaryDirectory();
        var oldPath = System.IO.Path.Combine(temporary.Path, "old.jsonl");
        var newPath = System.IO.Path.Combine(temporary.Path, "new.jsonl");
        File.WriteAllText(oldPath, ClaudeLine("old", "request", 1));
        File.WriteAllText(newPath, ClaudeLine("new", "request", 2));
        File.SetLastWriteTimeUtc(oldPath, DateTime.UtcNow.AddDays(-10));

        var files = LocalUsageReader.EnumerateJsonlFiles(
            temporary.Path,
            DateTimeOffset.UtcNow.AddDays(-1));

        Assert.DoesNotContain(oldPath, files, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(newPath, files, StringComparer.OrdinalIgnoreCase);
    }

    private static UsageEntry Entry(string id, DateTimeOffset date, long input) =>
        new(
            id,
            date,
            date.ToString("yyyy-MM-dd"),
            "claude-opus-4-8",
            input,
            0,
            0,
            0);

    private static string ClaudeLine(
        string id,
        string requestId,
        int output,
        string? model = "claude-opus-4-8")
    {
        var modelProperty = model is null ? string.Empty : $"\"model\":\"{model}\",";
        return "{" +
               "\"type\":\"assistant\"," +
               $"\"requestId\":\"{requestId}\"," +
               "\"timestamp\":\"2026-07-22T02:11:05.123Z\"," +
               "\"message\":{" +
               $"\"id\":\"{id}\"," +
               modelProperty +
               "\"usage\":{" +
               "\"input_tokens\":4," +
               $"\"output_tokens\":{output}," +
               "\"cache_creation_input_tokens\":1024," +
               "\"cache_read_input_tokens\":88231}}}";
    }
}
