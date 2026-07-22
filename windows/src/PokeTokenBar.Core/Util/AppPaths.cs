namespace PokeTokenBar.Core.Util;

public sealed class AppPaths
{
    public AppPaths(string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        RootDirectory = Path.GetFullPath(rootDirectory);
    }

    public static AppPaths Default { get; } = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokeTokenBar"));

    public string RootDirectory { get; }

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string CompanionStateFile => Path.Combine(RootDirectory, "companion-state.json");

    public string UsageCacheFile => Path.Combine(RootDirectory, "usage-cache.json");

    public string BaseIndexFile => Path.Combine(RootDirectory, "base-index.json");

    public string LastSnapshotFile => Path.Combine(RootDirectory, "last-snapshot.json");

    public string SpritesDirectory => Path.Combine(RootDirectory, "sprites");

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public string LogFile => Path.Combine(LogsDirectory, "app.log");

    public string OldLogFile => Path.Combine(LogsDirectory, "app.old.log");

    public string RunningMarkerFile => Path.Combine(LogsDirectory, "app.running");

    public void EnsureCreated()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(SpritesDirectory);
    }
}
