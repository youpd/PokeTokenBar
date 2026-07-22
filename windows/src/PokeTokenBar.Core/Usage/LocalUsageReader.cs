using System.Globalization;
using System.Text;
using System.Text.Json;
using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Usage;

public static class LocalUsageReader
{
    public static string DefaultClaudeProjectsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude",
        "projects");

    public static string DefaultCodexHomeDirectory => ResolveCodexHomeDirectories(
        Environment.GetEnvironmentVariable("CODEX_HOME"),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        [],
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData))[0];

    public static string DefaultCodexSessionsDirectory => Path.Combine(
        DefaultCodexHomeDirectory,
        "sessions");

    public static string DefaultGeminiTemporaryDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".gemini",
        "tmp");

    public static IReadOnlyList<string> ResolveCodexUsageDirectories(
        string? configuredCodexHome,
        string userProfile,
        IEnumerable<string>? extraHomes = null,
        string? roamingAppData = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userProfile);

        var codexHomes = ResolveCodexHomeDirectories(
            configuredCodexHome,
            userProfile,
            extraHomes ?? [],
            roamingAppData);
        var directories = new List<string>(codexHomes.Count * 2);
        foreach (var home in codexHomes)
        {
            directories.Add(Path.Combine(home, "sessions"));
            directories.Add(Path.Combine(home, "archived_sessions"));
        }

        return directories;
    }

    private static IReadOnlyList<string> ResolveCodexHomeDirectories(
        string? configuredCodexHome,
        string userProfile,
        IEnumerable<string> extraHomes,
        string? roamingAppData)
    {
        var homes = new List<string>();
        if (!string.IsNullOrWhiteSpace(configuredCodexHome))
        {
            foreach (var home in configuredCodexHome.Split(
                         ',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                AddNormalizedPath(homes, home);
            }
        }

        if (homes.Count == 0)
        {
            AddNormalizedPath(homes, PreferredDefaultCodexHome(userProfile, roamingAppData));
        }

        foreach (var extraHome in extraHomes.Where(home =>
                     !string.IsNullOrWhiteSpace(home)))
        {
            AddNormalizedPath(homes, Path.Combine(extraHome, ".codex"));
        }

        return homes;
    }

    private static string PreferredDefaultCodexHome(
        string userProfile,
        string? roamingAppData)
    {
        var defaultHome = Path.Combine(userProfile, ".codex");
        if (string.IsNullOrWhiteSpace(roamingAppData))
        {
            return defaultHome;
        }

        var windowsAppHome = Path.Combine(
            roamingAppData,
            "orca",
            "codex-runtime-home",
            "home");
        var defaultWrite = LatestCodexSessionWrite(defaultHome);
        var appWrite = LatestCodexSessionWrite(windowsAppHome);
        return appWrite is not null && (defaultWrite is null || appWrite > defaultWrite)
            ? windowsAppHome
            : defaultHome;
    }

    private static DateTime? LatestCodexSessionWrite(string codexHome)
    {
        DateTime? latest = null;
        foreach (var directoryName in new[] { "sessions", "archived_sessions" })
        {
            var directory = Path.Combine(codexHome, directoryName);
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                foreach (var file in Directory.EnumerateFiles(
                             directory,
                             "rollout-*.jsonl",
                             SearchOption.AllDirectories))
                {
                    try
                    {
                        var modified = File.GetLastWriteTimeUtc(file);
                        if (latest is null || modified > latest)
                        {
                            latest = modified;
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        // A live Codex process can rotate a session during discovery.
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // An unavailable candidate should not block the remaining homes.
            }
        }

        return latest;
    }

    private static void AddNormalizedPath(List<string> paths, string path)
    {
        try
        {
            var normalized = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(path));
            if (!paths.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            {
                paths.Add(normalized);
            }
        }
        catch (Exception exception) when (
            exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Ignore an invalid optional home and continue with the remaining roots.
        }
    }

    public static IReadOnlyList<UsageEntry> ParseClaudeFile(
        string filePath,
        TimeZoneInfo? timeZone = null)
    {
        var entries = new List<UsageEntry>();
        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            while (reader.ReadLine() is { } line)
            {
                if (!line.Contains("\"usage\"", StringComparison.Ordinal) ||
                    !line.Contains("\"assistant\"", StringComparison.Ordinal))
                {
                    continue;
                }

                if (TryParseClaudeLine(line, timeZone ?? TimeZoneInfo.Local, out var entry))
                {
                    entries.Add(entry);
                }
            }
        }
        catch (IOException)
        {
            return [];
        }
        catch (UnauthorizedAccessException)
        {
            return [];
        }

        return DedupKeepMax(entries);
    }

    public static IReadOnlyList<UsageEntry> ClaudeEntries(
        string rootDirectory,
        DateTimeOffset modifiedSince,
        TimeZoneInfo? timeZone = null)
    {
        var entries = new List<UsageEntry>();
        foreach (var file in EnumerateJsonlFiles(rootDirectory, modifiedSince))
        {
            entries.AddRange(ParseClaudeFile(file, timeZone));
        }

        return DedupKeepMax(entries);
    }

    public static IReadOnlyList<string> EnumerateJsonlFiles(
        string rootDirectory,
        DateTimeOffset modifiedSince) =>
        EnumerateUsageFiles(rootDirectory, modifiedSince, allowJson: false);

    public static IReadOnlyList<string> EnumerateUsageFiles(
        string rootDirectory,
        DateTimeOffset modifiedSince,
        bool allowJson)
    {
        if (!Directory.Exists(rootDirectory))
        {
            return [];
        }

        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(Path.GetFullPath(rootDirectory));

        while (pending.TryPop(out var directory))
        {
            try
            {
                foreach (var file in Directory.EnumerateFiles(directory))
                {
                    try
                    {
                        var extension = Path.GetExtension(file);
                        if (!extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase) &&
                            !(allowJson && extension.Equals(".json", StringComparison.OrdinalIgnoreCase)))
                        {
                            continue;
                        }

                        var modified = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                        if (modified >= modifiedSince.ToUniversalTime())
                        {
                            files.Add(Path.GetFullPath(file));
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        // A live CLI may rotate a file between enumeration and metadata lookup.
                    }
                }

                foreach (var child in Directory.EnumerateDirectories(directory))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        {
                            pending.Push(child);
                        }
                    }
                    catch (Exception exception) when (
                        exception is IOException or UnauthorizedAccessException)
                    {
                        // Ignore an inaccessible child and continue scanning its siblings.
                    }
                }
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Ignore an inaccessible directory and continue with the remaining stack.
            }
        }

        files.Sort(StringComparer.OrdinalIgnoreCase);
        return files;
    }

    public static IReadOnlyList<UsageEntry> ParseCodexFile(
        string filePath,
        TimeZoneInfo? timeZone = null)
    {
        var entries = new List<UsageEntry>();
        var zone = timeZone ?? TimeZoneInfo.Local;
        var model = "codex";
        var turn = 0;

        try
        {
            using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(
                stream,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true);

            while (reader.ReadLine() is { } line)
            {
                if (line.Contains("\"model\"", StringComparison.Ordinal) &&
                    TryReadCodexModel(line, out var parsedModel))
                {
                    model = parsedModel;
                }

                if (!line.Contains("token_count", StringComparison.Ordinal) ||
                    !TryParseCodexLine(
                        line,
                        Path.GetFileName(filePath),
                        turn,
                        model,
                        zone,
                        out var entry))
                {
                    continue;
                }

                entries.Add(entry);
                turn++;
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return entries;
    }

    public static IReadOnlyList<UsageEntry> ParseGeminiFile(
        string filePath,
        TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        try
        {
            return Path.GetExtension(filePath).Equals(".jsonl", StringComparison.OrdinalIgnoreCase)
                ? ParseGeminiJsonLines(filePath, zone)
                : ParseGeminiLegacyJson(filePath, zone);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return [];
        }
    }

    public static IReadOnlyList<UsageEntry> DedupKeepMax(IEnumerable<UsageEntry> entries)
    {
        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (!byId.TryGetValue(entry.Id, out var existing) || entry.Total > existing.Total)
            {
                byId[entry.Id] = entry;
            }
        }

        return byId.Values.ToArray();
    }

    public static DailyUsage? Daily(IEnumerable<UsageEntry> entries, string localDay)
    {
        var bucket = new UsageBucket();
        foreach (var entry in entries.Where(entry => entry.LocalDay == localDay))
        {
            bucket.Add(entry);
        }

        return bucket.Total == 0
            ? null
            : new DailyUsage(
                localDay,
                bucket.Input,
                bucket.Output,
                bucket.CacheWrite,
                bucket.CacheRead,
                bucket.Total,
                bucket.Cost);
    }

    public static PeriodUsage Period(
        IEnumerable<UsageEntry> entries,
        string periodKey,
        string fromDay,
        string toDay)
    {
        var bucket = new UsageBucket();
        foreach (var entry in entries.Where(entry =>
                     string.CompareOrdinal(entry.LocalDay, fromDay) >= 0 &&
                     string.CompareOrdinal(entry.LocalDay, toDay) <= 0))
        {
            bucket.Add(entry);
        }

        return new PeriodUsage(periodKey, bucket.Total, bucket.Cost);
    }

    public static BlockUsage? ActiveBlock(
        IEnumerable<UsageEntry> entries,
        DateTimeOffset now)
    {
        var windowStart = now.AddHours(-5);
        var recent = entries
            .Where(entry => entry.Date >= windowStart && entry.Date <= now)
            .OrderBy(entry => entry.Date)
            .ToArray();
        if (recent.Length == 0)
        {
            return null;
        }

        var bucket = new UsageBucket();
        foreach (var entry in recent)
        {
            bucket.Add(entry);
        }

        var first = recent[0].Date;
        var minutes = Math.Max(1, (now - first).TotalMinutes);
        return new BlockUsage(
            $"block-{first.ToUnixTimeSeconds()}",
            first,
            first.AddHours(5),
            true,
            bucket.Total,
            bucket.Cost,
            bucket.Total / minutes);
    }

    public static bool TryParseTimestamp(string value, out DateTimeOffset timestamp)
    {
        if (DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out timestamp))
        {
            return true;
        }

        var dot = value.IndexOf('.', StringComparison.Ordinal);
        if (dot >= 0)
        {
            var zone = value.IndexOfAny(['+', '-', 'Z'], dot + 1);
            if (zone > dot)
            {
                var fraction = value.AsSpan(dot + 1, zone - dot - 1);
                var milliseconds = fraction[..Math.Min(3, fraction.Length)].ToString().PadRight(3, '0');
                var rebuilt = value[..(dot + 1)] + milliseconds + value[zone..];
                if (DateTimeOffset.TryParse(
                        rebuilt,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind,
                        out timestamp))
                {
                    return true;
                }
            }
        }

        timestamp = default;
        return false;
    }

    public static string TodayKey(
        DateTimeOffset? now = null,
        TimeZoneInfo? timeZone = null) =>
        ToDayKey(now ?? DateTimeOffset.Now, timeZone ?? TimeZoneInfo.Local);

    public static string MonthKey(
        DateTimeOffset date,
        TimeZoneInfo? timeZone = null) =>
        TimeZoneInfo.ConvertTime(date, timeZone ?? TimeZoneInfo.Local)
            .ToString("yyyy-MM", CultureInfo.InvariantCulture);

    public static DateTimeOffset StartOfDay(
        DateTimeOffset date,
        TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(date, zone).Date;
        return FromLocalDate(local, zone);
    }

    public static DateTimeOffset StartOfMonth(
        DateTimeOffset date,
        TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(date, zone);
        return FromLocalDate(new DateTime(local.Year, local.Month, 1), zone);
    }

    public static DateTimeOffset StartOfWeek(
        DateTimeOffset date,
        CultureInfo? culture = null,
        TimeZoneInfo? timeZone = null)
    {
        var zone = timeZone ?? TimeZoneInfo.Local;
        var local = TimeZoneInfo.ConvertTime(date, zone).Date;
        var firstDay = (culture ?? CultureInfo.CurrentCulture).DateTimeFormat.FirstDayOfWeek;
        var difference = (7 + (local.DayOfWeek - firstDay)) % 7;
        return FromLocalDate(local.AddDays(-difference), zone);
    }

    public static string ToDayKey(DateTimeOffset date, TimeZoneInfo timeZone) =>
        TimeZoneInfo.ConvertTime(date, timeZone)
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static bool TryParseClaudeLine(
        string line,
        TimeZoneInfo timeZone,
        out UsageEntry entry)
    {
        entry = null!;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGetString(root, "type", out var type) || type != "assistant" ||
                !root.TryGetProperty("message", out var message) ||
                message.ValueKind != JsonValueKind.Object ||
                !message.TryGetProperty("usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "timestamp", out var timestampValue) ||
                !TryParseTimestamp(timestampValue, out var timestamp))
            {
                return false;
            }

            var model = TryGetString(message, "model", out var modelValue)
                ? modelValue
                : "unknown";
            var messageId = TryGetString(message, "id", out var messageIdValue)
                ? messageIdValue
                : string.Empty;
            var requestId = TryGetString(root, "requestId", out var requestIdValue)
                ? requestIdValue
                : string.Empty;

            entry = new UsageEntry(
                messageId + "|" + requestId,
                timestamp,
                ToDayKey(timestamp, timeZone),
                model,
                ReadInt64(usage, "input_tokens"),
                ReadInt64(usage, "output_tokens"),
                ReadInt64(usage, "cache_creation_input_tokens"),
                ReadInt64(usage, "cache_read_input_tokens"));
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadCodexModel(string line, out string model)
    {
        model = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(line);
            if (!document.RootElement.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (TryGetString(payload, "model", out model))
            {
                return true;
            }

            return payload.TryGetProperty("turn_context", out var turnContext) &&
                   turnContext.ValueKind == JsonValueKind.Object &&
                   TryGetString(turnContext, "model", out model);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryParseCodexLine(
        string line,
        string fileName,
        int turn,
        string model,
        TimeZoneInfo timeZone,
        out UsageEntry entry)
    {
        entry = null!;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("payload", out var payload) ||
                payload.ValueKind != JsonValueKind.Object ||
                !TryGetString(payload, "type", out var payloadType) ||
                payloadType != "token_count" ||
                !payload.TryGetProperty("info", out var info) ||
                info.ValueKind != JsonValueKind.Object ||
                !info.TryGetProperty("last_token_usage", out var usage) ||
                usage.ValueKind != JsonValueKind.Object ||
                !TryGetString(root, "timestamp", out var timestampValue) ||
                !TryParseTimestamp(timestampValue, out var timestamp))
            {
                return false;
            }

            var inputTotal = ReadInt64(usage, "input_tokens");
            var cacheRead = ReadInt64(usage, "cached_input_tokens");
            entry = new UsageEntry(
                $"codex|{fileName}|{turn}",
                timestamp,
                ToDayKey(timestamp, timeZone),
                model,
                Math.Max(0, inputTotal - cacheRead),
                ReadInt64(usage, "output_tokens"),
                0,
                cacheRead);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IReadOnlyList<UsageEntry> ParseGeminiJsonLines(
        string filePath,
        TimeZoneInfo timeZone)
    {
        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        DateTimeOffset? lastTimestamp = null;
        var lineNumber = 0;

        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(
            stream,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true);

        while (reader.ReadLine() is { } line)
        {
            lineNumber++;
            if (!line.Contains("\"tokens\"", StringComparison.Ordinal) &&
                !line.Contains("\"timestamp\"", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                if (TryGetString(root, "timestamp", out var timestampValue) &&
                    TryParseTimestamp(timestampValue, out var parsedTimestamp))
                {
                    lastTimestamp = parsedTimestamp;
                }

                AbsorbGemini(
                    root,
                    Path.GetFileName(filePath),
                    $"anonymous-{lineNumber}",
                    lastTimestamp,
                    timeZone,
                    byId,
                    order);
            }
            catch (JsonException)
            {
                // A damaged live line must not invalidate the rest of the session.
            }
        }

        return order.Select(id => byId[id]).ToArray();
    }

    private static IReadOnlyList<UsageEntry> ParseGeminiLegacyJson(
        string filePath,
        TimeZoneInfo timeZone)
    {
        using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        if (!root.TryGetProperty("messages", out var messages) ||
            messages.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        DateTimeOffset? sessionStart = null;
        if (TryGetString(root, "startTime", out var startValue) &&
            TryParseTimestamp(startValue, out var parsedStart))
        {
            sessionStart = parsedStart;
        }

        var byId = new Dictionary<string, UsageEntry>(StringComparer.Ordinal);
        var order = new List<string>();
        var index = 0;
        foreach (var message in messages.EnumerateArray())
        {
            AbsorbGemini(
                message,
                Path.GetFileName(filePath),
                $"anonymous-{index++}",
                sessionStart,
                timeZone,
                byId,
                order);
        }

        return order.Select(id => byId[id]).ToArray();
    }

    private static void AbsorbGemini(
        JsonElement record,
        string fileName,
        string anonymousId,
        DateTimeOffset? fallbackTimestamp,
        TimeZoneInfo timeZone,
        IDictionary<string, UsageEntry> byId,
        ICollection<string> order)
    {
        if (!record.TryGetProperty("tokens", out var tokens) ||
            tokens.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var id = TryGetString(record, "id", out var parsedId)
            ? parsedId
            : anonymousId;
        var timestamp = fallbackTimestamp;
        if (TryGetString(record, "timestamp", out var timestampValue) &&
            TryParseTimestamp(timestampValue, out var parsedTimestamp))
        {
            timestamp = parsedTimestamp;
        }

        if (timestamp is null)
        {
            return;
        }

        var input = ReadInt64(tokens, "input");
        var cached = ReadInt64(tokens, "cached");
        var entry = new UsageEntry(
            $"gemini|{fileName}|{id}",
            timestamp.Value,
            ToDayKey(timestamp.Value, timeZone),
            TryGetString(record, "model", out var model) ? model : "gemini",
            Math.Max(0, input - cached) + ReadInt64(tokens, "tool"),
            ReadInt64(tokens, "output") + ReadInt64(tokens, "thoughts"),
            0,
            cached);

        if (!byId.ContainsKey(id))
        {
            order.Add(id);
        }

        byId[id] = entry;
    }

    private static bool TryGetString(
        JsonElement element,
        string propertyName,
        out string value)
    {
        value = string.Empty;
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? string.Empty;
        return true;
    }

    private static long ReadInt64(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return 0;
        }

        if (property.TryGetInt64(out var integer))
        {
            return integer;
        }

        return property.TryGetDouble(out var number) ? (long)number : 0;
    }

    private static DateTimeOffset FromLocalDate(DateTime localDate, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(localDate, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, timeZone.GetUtcOffset(unspecified));
    }

    private sealed class UsageBucket
    {
        public long Input { get; private set; }

        public long Output { get; private set; }

        public long CacheWrite { get; private set; }

        public long CacheRead { get; private set; }

        public double Cost { get; private set; }

        public long Total => Input + Output + CacheWrite + CacheRead;

        public void Add(UsageEntry entry)
        {
            Input += entry.Input;
            Output += entry.Output;
            CacheWrite += entry.CacheWrite;
            CacheRead += entry.CacheRead;
            Cost += ModelPricing.Cost(
                entry.Model,
                entry.Input,
                entry.Output,
                entry.CacheWrite,
                entry.CacheRead);
        }
    }
}
