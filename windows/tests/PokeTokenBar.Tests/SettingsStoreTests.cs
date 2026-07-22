using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Tests;

public sealed class SettingsStoreTests
{
    [Fact]
    public void MissingFileReturnsDefaults()
    {
        using var temporary = new TemporaryDirectory();
        var store = new SettingsStore(Path.Combine(temporary.Path, "settings.json"));

        var settings = store.Load();

        Assert.Equal(120, settings.RefreshInterval);
        Assert.True(settings.ShowTokensInMenu);
    }

    [Fact]
    public void SaveAndLoadRoundTripsSettingsAtomically()
    {
        using var temporary = new TemporaryDirectory();
        var filePath = Path.Combine(temporary.Path, "nested", "settings.json");
        var store = new SettingsStore(filePath);
        var expected = new AppSettings
        {
            RefreshInterval = 300,
            WarnThreshold = 75,
            CritThreshold = 90,
            ShowTokensInMenu = false,
            ShowCostInMenu = true,
            ShowLimitInMenu = true,
            LimitNotifications = false,
            CompanionNotifications = false,
            UpdateNotificationsEnabled = false,
            StatusChecksEnabled = false,
            ClaudeLimitsDisabled = true,
            SkippedUpdateVersion = "0.2.0",
            CodexPath = @"C:\Tools\codex.cmd",
            ExtraHomes = [@"\\wsl.localhost\Ubuntu\home\me"],
            NumericTrayIcon = "tokens",
        };

        store.Save(expected);
        var actual = store.Load();

        Assert.Equal(expected.RefreshInterval, actual.RefreshInterval);
        Assert.Equal(expected.WarnThreshold, actual.WarnThreshold);
        Assert.Equal(expected.CritThreshold, actual.CritThreshold);
        Assert.Equal(expected.ShowTokensInMenu, actual.ShowTokensInMenu);
        Assert.Equal(expected.ShowCostInMenu, actual.ShowCostInMenu);
        Assert.Equal(expected.ShowLimitInMenu, actual.ShowLimitInMenu);
        Assert.Equal(expected.LimitNotifications, actual.LimitNotifications);
        Assert.Equal(expected.CompanionNotifications, actual.CompanionNotifications);
        Assert.Equal(expected.UpdateNotificationsEnabled, actual.UpdateNotificationsEnabled);
        Assert.Equal(expected.StatusChecksEnabled, actual.StatusChecksEnabled);
        Assert.Equal(expected.ClaudeLimitsDisabled, actual.ClaudeLimitsDisabled);
        Assert.Equal(expected.SkippedUpdateVersion, actual.SkippedUpdateVersion);
        Assert.Equal(expected.CodexPath, actual.CodexPath);
        Assert.Equal(expected.ExtraHomes, actual.ExtraHomes);
        Assert.Equal(expected.NumericTrayIcon, actual.NumericTrayIcon);
        Assert.False(File.Exists(filePath + ".tmp"));

        var json = File.ReadAllText(filePath);
        Assert.Contains("\"refreshInterval\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"RefreshInterval\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidJsonFallsBackToDefaults()
    {
        using var temporary = new TemporaryDirectory();
        var filePath = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(filePath, "{ definitely-not-json }");
        var store = new SettingsStore(filePath);

        var settings = store.Load();

        Assert.Equal(120, settings.RefreshInterval);
        Assert.True(settings.ShowTokensInMenu);
    }

    [Fact]
    public void UnknownKeysAreIgnoredForForwardCompatibility()
    {
        using var temporary = new TemporaryDirectory();
        var filePath = Path.Combine(temporary.Path, "settings.json");
        File.WriteAllText(filePath, "{\"refreshInterval\":60,\"futureSetting\":true}");
        var store = new SettingsStore(filePath);

        var settings = store.Load();

        Assert.Equal(60, settings.RefreshInterval);
    }
}
