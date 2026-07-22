using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Tests;

public sealed class GeminiUsageTests
{
    private const string NewJsonLines = """
        {"type":"session_metadata","sessionId":"s1","startTime":"2026-07-03T01:00:00.000Z"}
        {"type":"user","id":"m1","timestamp":"2026-07-03T01:00:05.000Z","content":[{"text":"hi"}]}
        {"type":"gemini","id":"m2","timestamp":"2026-07-03T01:00:10.000Z","model":"gemini-2.5-pro","tokens":{"input":1000,"output":50,"cached":600,"thoughts":30,"tool":20,"total":1100}}
        {"type":"gemini","id":"m3","timestamp":"2026-07-03T01:01:00.000Z","model":"gemini-2.5-flash","tokens":{"input":10,"output":5,"cached":0,"thoughts":0,"tool":0,"total":15}}
        {"type":"message_update","id":"m3","tokens":{"input":10,"output":8,"cached":0,"thoughts":2,"tool":0,"total":20}}
        """;

    private const string LegacyJson = """
        {"sessionId":"s0","startTime":"2026-07-02T00:00:00.000Z","messages":[
          {"id":"a1","type":"gemini","timestamp":"2026-07-02T00:10:00.000Z","model":"gemini-2.5-pro","tokens":{"input":100,"output":10,"cached":0,"thoughts":0,"tool":0,"total":110}},
          {"id":"a2","type":"user","content":[{"text":"x"}]}
        ]}
        """;

    [Fact]
    public void NewJsonLinesUsesUsageMetadataMappingAndLastUpdateWins()
    {
        using var temporary = new TemporaryDirectory();
        var chats = System.IO.Path.Combine(temporary.Path, "hash1", "chats");
        Directory.CreateDirectory(chats);
        var path = System.IO.Path.Combine(chats, "session.jsonl");
        File.WriteAllText(path, NewJsonLines);

        var entries = LocalUsageReader.ParseGeminiFile(path, TimeZoneInfo.Utc);

        Assert.Equal(2, entries.Count);
        var first = entries[0];
        Assert.Equal("gemini-2.5-pro", first.Model);
        Assert.Equal(420, first.Input);
        Assert.Equal(600, first.CacheRead);
        Assert.Equal(80, first.Output);
        Assert.Equal(0, first.CacheWrite);
        Assert.Equal(1_100, first.Total);
        var updated = entries[1];
        Assert.Equal(10, updated.Output);
        Assert.Equal(20, updated.Total);
        Assert.Equal(new DateTimeOffset(2026, 7, 3, 1, 1, 0, TimeSpan.Zero), updated.Date);
    }

    [Fact]
    public void LegacyJsonReadsMessagesAndSessionTimestampFallback()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "checkpoint.json");
        File.WriteAllText(path, LegacyJson);

        var entry = Assert.Single(LocalUsageReader.ParseGeminiFile(path, TimeZoneInfo.Utc));

        Assert.Equal(100, entry.Input);
        Assert.Equal(10, entry.Output);
        Assert.Equal(110, entry.Total);
        Assert.Equal("gemini-2.5-pro", entry.Model);
    }

    [Fact]
    public void CacheCollectsCodexAndBothGeminiExtensionsAcrossRestart()
    {
        using var temporary = new TemporaryDirectory();
        var codexRoot = System.IO.Path.Combine(temporary.Path, "sessions");
        var chats = System.IO.Path.Combine(temporary.Path, "tmp", "hash1", "chats");
        Directory.CreateDirectory(codexRoot);
        Directory.CreateDirectory(chats);
        File.WriteAllText(
            System.IO.Path.Combine(codexRoot, "rollout-a.jsonl"),
            CodexTokenLine());
        File.WriteAllText(System.IO.Path.Combine(chats, "session-a.jsonl"), NewJsonLines);
        File.WriteAllText(System.IO.Path.Combine(chats, "checkpoint-b.json"), LegacyJson);
        File.WriteAllText(System.IO.Path.Combine(temporary.Path, "tmp", "ignored.json"), LegacyJson);
        var cacheFile = System.IO.Path.Combine(temporary.Path, "usage-cache.json");
        var cache = new LocalUsageCache([], [codexRoot], [System.IO.Path.Combine(temporary.Path, "tmp")], cacheFile);

        Assert.Single(cache.CodexEntries(DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc));
        Assert.Equal(3, cache.GeminiEntries(DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc).Count);
        cache.Flush();

        var restored = new LocalUsageCache(
            [],
            [codexRoot],
            [System.IO.Path.Combine(temporary.Path, "tmp")],
            cacheFile);
        Assert.Single(restored.CodexEntries(DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc));
        Assert.Equal(3, restored.GeminiEntries(DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc).Count);
    }

    [Fact]
    public void FileWithoutTokensProducesNoEntries()
    {
        using var temporary = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporary.Path, "empty.json");
        File.WriteAllText(path, "{\"entries\":[{\"type\":\"user\"}]}");

        Assert.Empty(LocalUsageReader.ParseGeminiFile(path, TimeZoneInfo.Utc));
    }

    [Fact]
    public void GeminiPricingUsesExactFamilyAndUnknownRules()
    {
        Assert.Equal(1.25, ModelPricing.Cost("gemini-2.5-pro", 1_000_000, 0, 0, 0));
        Assert.Equal(0.30, ModelPricing.Cost("gemini-3-flash-preview", 1_000_000, 0, 0, 0));
        Assert.Equal(0, ModelPricing.Cost("gemini-nano-banana", 1_000_000, 0, 0, 0));
        var cost = ModelPricing.Cost("gemini-2.5-pro", 420, 80, 0, 600);
        Assert.Equal(
            (420 * 1.25e-6) + (80 * 10e-6) + (600 * 0.3125e-6),
            cost,
            precision: 12);
    }

    private static string CodexTokenLine() =>
        "{\"timestamp\":\"2026-07-03T01:00:00Z\",\"payload\":{\"type\":\"token_count\"," +
        "\"info\":{\"last_token_usage\":{\"input_tokens\":100," +
        "\"cached_input_tokens\":80,\"output_tokens\":7}}}}";
}
