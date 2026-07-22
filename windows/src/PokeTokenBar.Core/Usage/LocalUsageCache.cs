using System.IO.Compression;
using System.Text.Json;
using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Usage;

public sealed class LocalUsageCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly object _sync = new();
    private readonly IReadOnlyList<string> _claudeRoots;
    private readonly IReadOnlyList<string> _codexRoots;
    private readonly IReadOnlyList<string> _geminiRoots;
    private readonly string _cacheFile;
    private readonly Func<DateTimeOffset> _clock;
    private Dictionary<string, CacheBlob> _claude = NewBlobDictionary();
    private Dictionary<string, CacheBlob> _codex = NewBlobDictionary();
    private Dictionary<string, CacheBlob> _gemini = NewBlobDictionary();
    private bool _loaded;
    private bool _dirty;
    private DateTimeOffset? _lastSave;

    public LocalUsageCache(
        IEnumerable<string> claudeRoots,
        string cacheFile,
        Func<DateTimeOffset>? clock = null)
        : this(claudeRoots, [], [], cacheFile, clock)
    {
    }

    public LocalUsageCache(
        IEnumerable<string> claudeRoots,
        IEnumerable<string> codexRoots,
        IEnumerable<string> geminiRoots,
        string cacheFile,
        Func<DateTimeOffset>? clock = null)
    {
        ArgumentNullException.ThrowIfNull(claudeRoots);
        ArgumentNullException.ThrowIfNull(codexRoots);
        ArgumentNullException.ThrowIfNull(geminiRoots);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheFile);

        _claudeRoots = NormalizeRoots(claudeRoots);
        _codexRoots = NormalizeRoots(codexRoots);
        _geminiRoots = NormalizeRoots(geminiRoots);
        _cacheFile = Path.GetFullPath(cacheFile);
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public IReadOnlyList<UsageEntry> ClaudeEntries(
        DateTimeOffset modifiedSince,
        TimeZoneInfo? timeZone = null)
    {
        lock (_sync)
        {
            EnsureLoaded();
            var entries = Collect(
                _claudeRoots,
                modifiedSince,
                _claude,
                allowJson: false,
                _ => true,
                file => LocalUsageReader.ParseClaudeFile(file, timeZone));
            return LocalUsageReader.DedupKeepMax(entries);
        }
    }

    public IReadOnlyList<UsageEntry> CodexEntries(
        DateTimeOffset modifiedSince,
        TimeZoneInfo? timeZone = null)
    {
        lock (_sync)
        {
            EnsureLoaded();
            return Collect(
                _codexRoots,
                modifiedSince,
                _codex,
                allowJson: false,
                file => Path.GetFileName(file).StartsWith("rollout-", StringComparison.OrdinalIgnoreCase),
                file => LocalUsageReader.ParseCodexFile(file, timeZone));
        }
    }

    public IReadOnlyList<UsageEntry> GeminiEntries(
        DateTimeOffset modifiedSince,
        TimeZoneInfo? timeZone = null)
    {
        lock (_sync)
        {
            EnsureLoaded();
            return Collect(
                _geminiRoots,
                modifiedSince,
                _gemini,
                allowJson: true,
                file => string.Equals(
                    Directory.GetParent(file)?.Name,
                    "chats",
                    StringComparison.OrdinalIgnoreCase),
                file => LocalUsageReader.ParseGeminiFile(file, timeZone));
        }
    }

    public void Flush()
    {
        lock (_sync)
        {
            EnsureLoaded();
            SaveIfNeeded(force: true);
        }
    }

    private IReadOnlyList<UsageEntry> Collect(
        IEnumerable<string> roots,
        DateTimeOffset modifiedSince,
        IDictionary<string, CacheBlob> cache,
        bool allowJson,
        Func<string, bool> includeFile,
        Func<string, IReadOnlyList<UsageEntry>> parse)
    {
        EnsureLoaded();
        var entries = new List<UsageEntry>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            foreach (var file in LocalUsageReader.EnumerateUsageFiles(
                         root,
                         modifiedSince,
                         allowJson))
            {
                if (!seenFiles.Add(file) || !includeFile(file))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(file);
                    var modifiedTicks = info.LastWriteTimeUtc.Ticks;
                    var size = info.Length;
                    if (cache.TryGetValue(file, out var blob) &&
                        blob.ModifiedUtcTicks == modifiedTicks &&
                        blob.Size == size)
                    {
                        entries.AddRange(blob.Entries);
                        continue;
                    }

                    var parsed = parse(file).ToList();
                    cache[file] = new CacheBlob
                    {
                        ModifiedUtcTicks = modifiedTicks,
                        Size = size,
                        Entries = parsed,
                    };
                    _dirty = true;
                    entries.AddRange(parsed);
                }
                catch (Exception exception) when (
                    exception is IOException or UnauthorizedAccessException)
                {
                    // A live CLI can rotate a session between enumeration and opening.
                }
            }
        }

        SaveIfNeeded();
        return entries;
    }

    private void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        if (!File.Exists(_cacheFile))
        {
            return;
        }

        try
        {
            var raw = File.ReadAllBytes(_cacheFile);
            var json = TryDecompress(raw) ?? raw;
            var snapshot = JsonSerializer.Deserialize<CacheSnapshot>(json, JsonOptions);
            if (snapshot is not null)
            {
                _claude = CopyBlobs(snapshot.Claude);
                _codex = CopyBlobs(snapshot.Codex);
                _gemini = CopyBlobs(snapshot.Gemini);
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            _claude.Clear();
            _codex.Clear();
            _gemini.Clear();
        }
    }

    private void SaveIfNeeded(bool force = false)
    {
        if (!_dirty)
        {
            return;
        }

        var now = _clock();
        if (!force &&
            _lastSave is { } lastSave &&
            now - lastSave < TimeSpan.FromSeconds(60))
        {
            return;
        }

        Prune(now);
        var snapshot = new CacheSnapshot
        {
            Claude = _claude,
            Codex = _codex,
            Gemini = _gemini,
        };
        var json = JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions);
        var compressed = Compress(json);

        try
        {
            var directory = Path.GetDirectoryName(_cacheFile);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryFile = _cacheFile + ".tmp";
            File.WriteAllBytes(temporaryFile, compressed);
            File.Move(temporaryFile, _cacheFile, overwrite: true);
            _dirty = false;
            _lastSave = now;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            // Cache persistence is an optimization; a write failure must not break usage.
        }
    }

    private void Prune(DateTimeOffset now)
    {
        var cutoffTicks = now.UtcDateTime.AddDays(-40).Ticks;
        _claude = Pruned(_claude, cutoffTicks);
        _codex = Pruned(_codex, cutoffTicks);
        _gemini = Pruned(_gemini, cutoffTicks);
    }

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots) =>
        roots
            .Where(root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static Dictionary<string, CacheBlob> CopyBlobs(
        Dictionary<string, CacheBlob>? source) =>
        source is null
            ? NewBlobDictionary()
            : new Dictionary<string, CacheBlob>(source, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, CacheBlob> Pruned(
        IEnumerable<KeyValuePair<string, CacheBlob>> source,
        long cutoffTicks) =>
        source
            .Where(pair => pair.Value.ModifiedUtcTicks >= cutoffTicks)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, CacheBlob> NewBlobDictionary() =>
        new(StringComparer.OrdinalIgnoreCase);

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    private static byte[]? TryDecompress(byte[] data)
    {
        try
        {
            using var input = new MemoryStream(data);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            return output.ToArray();
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException)
        {
            return null;
        }
    }

    private sealed class CacheSnapshot
    {
        public Dictionary<string, CacheBlob>? Claude { get; set; }

        public Dictionary<string, CacheBlob>? Codex { get; set; }

        public Dictionary<string, CacheBlob>? Gemini { get; set; }
    }

    private sealed class CacheBlob
    {
        public long ModifiedUtcTicks { get; set; }

        public long Size { get; set; }

        public List<UsageEntry> Entries { get; set; } = [];
    }
}
