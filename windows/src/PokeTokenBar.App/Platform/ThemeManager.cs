using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace PokeTokenBar.App.Platform;

internal sealed class ThemeManager : IDisposable
{
    private readonly Application _application;
    private bool _disposed;

    public ThemeManager(Application application)
    {
        _application = application;
        Apply();
        SystemEvents.UserPreferenceChanged += SystemEvents_OnUserPreferenceChanged;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= SystemEvents_OnUserPreferenceChanged;
    }

    internal static bool ReadLightPreference()
    {
        using var key = Registry.CurrentUser.OpenSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    private void SystemEvents_OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) =>
        _application.Dispatcher.BeginInvoke(Apply);

    private void Apply()
    {
        var light = ReadLightPreference();
        Set("WindowBackgroundBrush", light ? "#FFF4F5F7" : "#FF181A1F");
        Set("CardBackgroundBrush", light ? "#FFFFFFFF" : "#FF23262D");
        Set("PrimaryTextBrush", light ? "#FF1C2027" : "#FFF4F5F7");
        Set("SecondaryTextBrush", light ? "#FF596273" : "#FFB7BDC8");
        Set("AccentBrush", light ? "#FF168653" : "#FF72D6A0");
    }

    private void Set(string key, string color)
    {
        var parsed = (Color)ColorConverter.ConvertFromString(color);
        if (_application.Resources[key] is SolidColorBrush brush)
        {
            if (brush.IsFrozen)
            {
                _application.Resources[key] = new SolidColorBrush(parsed);
            }
            else
            {
                brush.Color = parsed;
            }
        }
    }
}
