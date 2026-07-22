namespace PokeTokenBar.Core.Models;

public sealed class AppSettings
{
    public int RefreshInterval { get; set; } = 120;

    public int WarnThreshold { get; set; } = 80;

    public int CritThreshold { get; set; } = 95;

    public bool ShowTokensInMenu { get; set; } = true;

    public bool ShowCostInMenu { get; set; }

    public bool ShowLimitInMenu { get; set; }

    public bool LimitNotifications { get; set; } = true;

    public bool CompanionNotifications { get; set; } = true;

    public bool UpdateNotificationsEnabled { get; set; } = true;

    public bool StatusChecksEnabled { get; set; } = true;

    public bool ClaudeLimitsDisabled { get; set; }

    public string? SkippedUpdateVersion { get; set; }

    public string? CodexPath { get; set; }

    public List<string> ExtraHomes { get; set; } = [];

    public string? NumericTrayIcon { get; set; }
}
