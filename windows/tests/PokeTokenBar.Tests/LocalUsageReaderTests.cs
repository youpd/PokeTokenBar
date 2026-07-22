using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;
using System.Text.Json;

namespace PokeTokenBar.Tests;

public sealed class LocalUsageReaderTests
{
    [Fact]
    public void CodexUsageDirectoriesHonorConfiguredHomesAndIncludeArchives()
    {
        using var temporary = new TemporaryDirectory();
        var configuredOne = Path.Combine(temporary.Path, "configured-one");
        var configuredTwo = Path.Combine(temporary.Path, "configured-two");
        var extraProfile = Path.Combine(temporary.Path, "extra-profile");

        var directories = LocalUsageReader.ResolveCodexUsageDirectories(
            $"{configuredOne}, {configuredTwo}",
            Path.Combine(temporary.Path, "default-profile"),
            [extraProfile]);

        Assert.Equal(
        [
            Path.Combine(configuredOne, "sessions"),
            Path.Combine(configuredOne, "archived_sessions"),
            Path.Combine(configuredTwo, "sessions"),
            Path.Combine(configuredTwo, "archived_sessions"),
            Path.Combine(extraProfile, ".codex", "sessions"),
            Path.Combine(extraProfile, ".codex", "archived_sessions"),
        ],
            directories);
    }

    [Fact]
    public void CodexUsageDirectoriesFallBackToUserProfile()
    {
        using var temporary = new TemporaryDirectory();

        var directories = LocalUsageReader.ResolveCodexUsageDirectories(
            null,
            temporary.Path);

        Assert.Equal(
        [
            Path.Combine(temporary.Path, ".codex", "sessions"),
            Path.Combine(temporary.Path, ".codex", "archived_sessions"),
        ],
            directories);
    }

    [Fact]
    public void CodexUsageDirectoriesPreferTheActiveWindowsAppRuntime()
    {
        using var temporary = new TemporaryDirectory();
        var userProfile = Path.Combine(temporary.Path, "profile");
        var defaultSessions = Path.Combine(userProfile, ".codex", "sessions");
        var roamingAppData = Path.Combine(temporary.Path, "app-data");
        var appHome = Path.Combine(
            roamingAppData,
            "orca",
            "codex-runtime-home",
            "home");
        var appSessions = Path.Combine(appHome, "sessions");
        Directory.CreateDirectory(defaultSessions);
        Directory.CreateDirectory(appSessions);
        var oldSession = Path.Combine(defaultSessions, "rollout-old.jsonl");
        var currentSession = Path.Combine(appSessions, "rollout-current.jsonl");
        File.WriteAllText(oldSession, "{}");
        File.WriteAllText(currentSession, "{}");
        File.SetLastWriteTimeUtc(oldSession, new DateTime(2026, 7, 21, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(currentSession, new DateTime(2026, 7, 22, 0, 0, 0, DateTimeKind.Utc));

        var directories = LocalUsageReader.ResolveCodexUsageDirectories(
            null,
            userProfile,
            roamingAppData: roamingAppData);

        Assert.Equal(
        [
            appSessions,
            Path.Combine(appHome, "archived_sessions"),
        ],
            directories);
    }

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
        Assert.Equal(5, ModelPricing.Cost("gpt-5.6-sol", 1_000_000, 0, 0, 0));
        Assert.Equal(30, ModelPricing.Cost("gpt-5.6-sol", 0, 1_000_000, 0, 0));
        Assert.Equal(6.25, ModelPricing.Cost("gpt-5.6-sol", 0, 0, 1_000_000, 0));
        Assert.Equal(0.5, ModelPricing.Cost("gpt-5.6-sol", 0, 0, 0, 1_000_000));
        Assert.Equal(2.5, ModelPricing.Cost("gpt-5.6-terra", 1_000_000, 0, 0, 0));
        Assert.Equal(1, ModelPricing.Cost("gpt-5.6-luna", 1_000_000, 0, 0, 0));
        Assert.Equal(2.5, ModelPricing.Cost("gpt-5.4", 1_000_000, 0, 0, 0));
        Assert.Equal(1.75, ModelPricing.Cost("gpt-5.3-codex", 1_000_000, 0, 0, 0));
        Assert.Equal(0, ModelPricing.Cost("unknown", 1_000_000, 0, 0, 0));
    }

    [Fact]
    public void CodexParserTracksModelAndMapsCachedInput()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "rollout-session.jsonl");
        File.WriteAllLines(path,
        [
            "{\"timestamp\":\"2026-07-22T01:00:00.000Z\",\"payload\":{" +
            "\"type\":\"turn_context\",\"model\":\"gpt-5.5-codex\"}}",
            "{\"type\":\"event_msg\",\"timestamp\":\"2026-07-22T01:01:00.000Z\"," +
            "\"payload\":{\"type\":\"token_count\",\"info\":{\"last_token_usage\":{" +
            "\"input_tokens\":1000,\"cached_input_tokens\":200,\"output_tokens\":50," +
            "\"reasoning_output_tokens\":10,\"total_tokens\":1050}}}}",
        ]);

        var entry = Assert.Single(LocalUsageReader.ParseCodexFile(path, TimeZoneInfo.Utc));

        Assert.Equal("codex|rollout-session.jsonl|0", entry.Id);
        Assert.Equal("gpt-5.5-codex", entry.Model);
        Assert.Equal(800, entry.Input);
        Assert.Equal(200, entry.CacheRead);
        Assert.Equal(50, entry.Output);
        Assert.Equal(0, entry.CacheWrite);
    }

    [Fact]
    public void CodexParserReadsNestedTurnContextModelAndUsesTurnIds()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "rollout-nested.jsonl");
        File.WriteAllLines(path,
        [
            "{\"payload\":{\"turn_context\":{\"model\":\"gpt-nested\"}}}",
            CodexTokenLine(100, 80, 7, "2026-07-22T01:01:00Z"),
            "damaged token_count line",
            CodexTokenLine(20, 30, 2, "2026-07-22T01:02:00Z"),
        ]);

        var entries = LocalUsageReader.ParseCodexFile(path, TimeZoneInfo.Utc);

        Assert.Equal(2, entries.Count);
        Assert.Equal("codex|rollout-nested.jsonl|0", entries[0].Id);
        Assert.Equal("codex|rollout-nested.jsonl|1", entries[1].Id);
        Assert.All(entries, entry => Assert.Equal("gpt-nested", entry.Model));
        Assert.Equal(0, entries[1].Input);
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

    private static string CodexTokenLine(
        int input,
        int cached,
        int output,
        string timestamp) =>
        JsonSerializer.Serialize(new
        {
            timestamp,
            payload = new
            {
                type = "token_count",
                info = new
                {
                    last_token_usage = new
                    {
                        input_tokens = input,
                        cached_input_tokens = cached,
                        output_tokens = output,
                    },
                },
            },
        });
}
