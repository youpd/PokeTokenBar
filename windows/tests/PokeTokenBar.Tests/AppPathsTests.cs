using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void PathsMatchWindowsStorageContract()
    {
        using var temporary = new TemporaryDirectory();
        var paths = new AppPaths(temporary.Path);

        Assert.Equal(Path.Combine(temporary.Path, "settings.json"), paths.SettingsFile);
        Assert.Equal(Path.Combine(temporary.Path, "companion-state.json"), paths.CompanionStateFile);
        Assert.Equal(Path.Combine(temporary.Path, "usage-cache.json"), paths.UsageCacheFile);
        Assert.Equal(Path.Combine(temporary.Path, "base-index.json"), paths.BaseIndexFile);
        Assert.Equal(Path.Combine(temporary.Path, "last-snapshot.json"), paths.LastSnapshotFile);
        Assert.Equal(Path.Combine(temporary.Path, "sprites"), paths.SpritesDirectory);
        Assert.Equal(Path.Combine(temporary.Path, "logs", "app.log"), paths.LogFile);
    }

    [Fact]
    public void EnsureCreatedMakesRequiredDirectories()
    {
        using var temporary = new TemporaryDirectory(create: false);
        var paths = new AppPaths(temporary.Path);

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.RootDirectory));
        Assert.True(Directory.Exists(paths.LogsDirectory));
        Assert.True(Directory.Exists(paths.SpritesDirectory));
    }
}
