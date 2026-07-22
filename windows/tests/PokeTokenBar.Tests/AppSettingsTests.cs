using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void DefaultsMatchPlan()
    {
        var settings = new AppSettings();

        Assert.Equal(120, settings.RefreshInterval);
        Assert.Equal(80, settings.WarnThreshold);
        Assert.Equal(95, settings.CritThreshold);
        Assert.True(settings.ShowTokensInMenu);
        Assert.False(settings.ShowCostInMenu);
        Assert.False(settings.ShowLimitInMenu);
        Assert.True(settings.LimitNotifications);
        Assert.True(settings.CompanionNotifications);
        Assert.True(settings.UpdateNotificationsEnabled);
        Assert.True(settings.StatusChecksEnabled);
        Assert.False(settings.ClaudeLimitsDisabled);
        Assert.Null(settings.SkippedUpdateVersion);
        Assert.Null(settings.CodexPath);
        Assert.Empty(settings.ExtraHomes);
        Assert.Null(settings.NumericTrayIcon);
    }
}
