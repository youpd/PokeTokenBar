using System.IO.Compression;
using System.Text;
using PokeTokenBar.Core.Usage;

namespace PokeTokenBar.Tests;

public sealed class LocalUsageCacheTests
{
    [Fact]
    public void UnchangedMetadataReusesCachedEntries()
    {
        using var fixture = new CacheFixture();
        var modified = DateTime.UtcNow.AddHours(-1);
        fixture.Write("a.jsonl", ClaudeLine(111), modified);
        var cache = fixture.CreateCache();

        Assert.Equal(111, Assert.Single(cache.ClaudeEntries(DateTimeOffset.UnixEpoch)).Output);
        fixture.Write("a.jsonl", ClaudeLine(222), modified);

        Assert.Equal(111, Assert.Single(cache.ClaudeEntries(DateTimeOffset.UnixEpoch)).Output);
    }

    [Fact]
    public void ChangedMetadataReparsesFile()
    {
        using var fixture = new CacheFixture();
        fixture.Write("a.jsonl", ClaudeLine(111), DateTime.UtcNow.AddHours(-1));
        var cache = fixture.CreateCache();
        _ = cache.ClaudeEntries(DateTimeOffset.UnixEpoch);

        fixture.Write("a.jsonl", ClaudeLine(999), DateTime.UtcNow);

        Assert.Equal(999, Assert.Single(cache.ClaudeEntries(DateTimeOffset.UnixEpoch)).Output);
    }

    [Fact]
    public void CompressedDiskSnapshotRoundTripsAcrossInstances()
    {
        using var fixture = new CacheFixture();
        var modified = DateTime.UtcNow.AddHours(-1);
        fixture.Write("a.jsonl", ClaudeLine(42), modified);
        _ = fixture.CreateCache().ClaudeEntries(DateTimeOffset.UnixEpoch);

        var raw = File.ReadAllBytes(fixture.CacheFile);
        Assert.StartsWith("{", Encoding.UTF8.GetString(Decompress(raw)));
        fixture.Write("a.jsonl", ClaudeLine(43), modified);

        Assert.Equal(
            42,
            Assert.Single(fixture.CreateCache().ClaudeEntries(DateTimeOffset.UnixEpoch)).Output);
    }

    [Fact]
    public void LegacyPlainJsonSnapshotStillLoads()
    {
        using var fixture = new CacheFixture();
        var modified = DateTime.UtcNow.AddHours(-1);
        fixture.Write("a.jsonl", ClaudeLine(42), modified);
        _ = fixture.CreateCache().ClaudeEntries(DateTimeOffset.UnixEpoch);
        File.WriteAllBytes(fixture.CacheFile, Decompress(File.ReadAllBytes(fixture.CacheFile)));
        fixture.Write("a.jsonl", ClaudeLine(43), modified);

        Assert.Equal(
            42,
            Assert.Single(fixture.CreateCache().ClaudeEntries(DateTimeOffset.UnixEpoch)).Output);
    }

    [Fact]
    public void SaveIsThrottledForSixtySeconds()
    {
        using var fixture = new CacheFixture();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var cache = fixture.CreateCache(() => now);
        fixture.Write("a.jsonl", ClaudeLine(1), now.UtcDateTime);
        _ = cache.ClaudeEntries(DateTimeOffset.UnixEpoch);
        var first = File.ReadAllBytes(fixture.CacheFile);

        now = now.AddSeconds(30);
        fixture.Write("a.jsonl", ClaudeLine(2), now.UtcDateTime);
        _ = cache.ClaudeEntries(DateTimeOffset.UnixEpoch);
        Assert.Equal(first, File.ReadAllBytes(fixture.CacheFile));

        now = now.AddSeconds(61);
        _ = cache.ClaudeEntries(DateTimeOffset.UnixEpoch);
        Assert.NotEqual(first, File.ReadAllBytes(fixture.CacheFile));
    }

    [Fact]
    public void FlushPersistsPendingChangesDuringShutdown()
    {
        using var fixture = new CacheFixture();
        var now = new DateTimeOffset(2026, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var cache = fixture.CreateCache(() => now);
        fixture.Write("a.jsonl", ClaudeLine(1), now.UtcDateTime);
        _ = cache.ClaudeEntries(DateTimeOffset.UnixEpoch);
        var first = File.ReadAllBytes(fixture.CacheFile);

        now = now.AddSeconds(30);
        fixture.Write("a.jsonl", ClaudeLine(2), now.UtcDateTime);
        _ = cache.ClaudeEntries(DateTimeOffset.UnixEpoch);
        cache.Flush();

        Assert.NotEqual(first, File.ReadAllBytes(fixture.CacheFile));
    }

    [Fact]
    public void SavePrunesBlobsOlderThanFortyDays()
    {
        using var fixture = new CacheFixture();
        var now = DateTimeOffset.UtcNow;
        fixture.Write("old.jsonl", ClaudeLine(1, "old"), now.AddDays(-45).UtcDateTime);
        fixture.Write("new.jsonl", ClaudeLine(2, "new"), now.UtcDateTime);
        var entries = fixture.CreateCache(() => now)
            .ClaudeEntries(DateTimeOffset.UnixEpoch);

        Assert.Equal(2, entries.Count);
        var json = Encoding.UTF8.GetString(Decompress(File.ReadAllBytes(fixture.CacheFile)));
        Assert.DoesNotContain("old.jsonl", json);
        Assert.Contains("new.jsonl", json);
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var zlib = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        zlib.CopyTo(output);
        return output.ToArray();
    }

    private static string ClaudeLine(int output, string id = "message") =>
        "{" +
        "\"type\":\"assistant\"," +
        "\"requestId\":\"request\"," +
        "\"timestamp\":\"2026-07-22T02:11:05.123Z\"," +
        "\"message\":{" +
        $"\"id\":\"{id}\"," +
        "\"model\":\"claude-opus-4-8\"," +
        "\"usage\":{" +
        "\"input_tokens\":10," +
        $"\"output_tokens\":{output}," +
        "\"cache_creation_input_tokens\":5," +
        "\"cache_read_input_tokens\":100}}}";

    private sealed class CacheFixture : IDisposable
    {
        private readonly TemporaryDirectory _temporary = new();

        public CacheFixture()
        {
            Root = System.IO.Path.Combine(_temporary.Path, "projects");
            Directory.CreateDirectory(Root);
            CacheFile = System.IO.Path.Combine(_temporary.Path, "usage-cache.json");
        }

        public string Root { get; }

        public string CacheFile { get; }

        public LocalUsageCache CreateCache(Func<DateTimeOffset>? clock = null) =>
            new([Root], CacheFile, clock);

        public void Write(string name, string content, DateTime modifiedUtc)
        {
            var path = System.IO.Path.Combine(Root, name);
            File.WriteAllText(path, content);
            File.SetLastWriteTimeUtc(path, modifiedUtc);
        }

        public void Dispose() => _temporary.Dispose();
    }
}
