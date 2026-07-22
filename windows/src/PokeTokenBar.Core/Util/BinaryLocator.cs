namespace PokeTokenBar.Core.Util;

public sealed class BinaryLocator
{
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<string, bool> _fileExists;
    private string? _cachedCodex;
    private DateTimeOffset? _nullCacheUntil;

    public BinaryLocator(
        Func<DateTimeOffset>? clock = null,
        Func<string, bool>? fileExists = null)
    {
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _fileExists = fileExists ?? File.Exists;
    }

    public string? LocateCodex(string? manualPath = null)
    {
        if (_cachedCodex is not null && _fileExists(_cachedCodex))
        {
            return _cachedCodex;
        }

        if (_nullCacheUntil is { } until && _clock() < until)
        {
            return null;
        }

        foreach (var candidate in Candidates(manualPath))
        {
            if (string.Equals(Path.GetExtension(candidate), ".ps1", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (_fileExists(candidate))
            {
                _cachedCodex = Path.GetFullPath(candidate);
                _nullCacheUntil = null;
                return _cachedCodex;
            }
        }

        _nullCacheUntil = _clock().AddMinutes(10);
        return null;
    }

    public static IReadOnlyList<string> CommonToolDirectories()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var directories = new List<string>
        {
            Path.Combine(roaming, "npm"),
            Path.Combine(profile, "scoop", "shims"),
            Path.Combine(local, "Microsoft", "WinGet", "Links"),
            Path.Combine(profile, ".bun", "bin"),
            Path.Combine(local, "pnpm"),
            Path.Combine(local, "Volta", "bin"),
            Path.Combine(programFiles, "nodejs"),
        };

        var nvmRoot = Path.Combine(roaming, "nvm");
        if (Directory.Exists(nvmRoot))
        {
            try
            {
                directories.AddRange(Directory.EnumerateDirectories(nvmRoot)
                    .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase));
            }
            catch (IOException)
            {
                // A concurrently changing nvm installation is optional.
            }
            catch (UnauthorizedAccessException)
            {
                // A concurrently changing nvm installation is optional.
            }
        }

        return directories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private IEnumerable<string> Candidates(string? manualPath)
    {
        if (!string.IsNullOrWhiteSpace(manualPath))
        {
            yield return Environment.ExpandEnvironmentVariables(manualPath.Trim().Trim('"'));
        }

        var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? ".EXE;.CMD;.BAT")
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(extension => extension is ".EXE" or ".CMD" or ".BAT" ||
                extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            foreach (var extension in extensions)
            {
                yield return Path.Combine(directory.Trim('"'), "codex" + extension.ToLowerInvariant());
            }
        }

        foreach (var directory in CommonToolDirectories())
        {
            foreach (var extension in new[] { ".exe", ".cmd", ".bat" })
            {
                yield return Path.Combine(directory, "codex" + extension);
            }
        }
    }
}
