using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using PokeTokenBar.App.Views;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Tray;

internal sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly FlyoutWindow _flyout = new();
    private readonly TaskbarIcon _trayIcon;
    private SettingsWindow? _settingsWindow;

    public TrayController(AppSettings settings, SettingsStore settingsStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "PokeTokenBar — 알 (준비 중)",
            IconSource = new GeneratedIconSource
            {
                Text = "🥚",
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 40,
                Background = Brushes.Transparent,
            },
            ContextMenu = BuildContextMenu(),
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => ToggleFlyout();
    }

    public void Start()
    {
        _trayIcon.ForceCreate();
        AppLog.Write("tray icon created");
    }

#if DEBUG
    internal void ShowFlyoutForSmokeTest()
    {
        ShowFlyout(keepOpenWhenDeactivated: true);
    }
#endif

    public void Dispose()
    {
        _flyout.CloseForShutdown();
        _settingsWindow?.Close();
        _trayIcon.Dispose();
    }

    private ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();

        var openItem = new MenuItem { Header = "열기" };
        openItem.Click += (_, _) => ShowFlyout();
        menu.Items.Add(openItem);

        var refreshItem = new MenuItem { Header = "지금 새로고침" };
        refreshItem.Click += (_, _) => AppLog.Write("manual refresh requested (M0 skeleton)");
        menu.Items.Add(refreshItem);

        var settingsItem = new MenuItem { Header = "설정" };
        settingsItem.Click += (_, _) => ShowSettings();
        menu.Items.Add(settingsItem);

        menu.Items.Add(new Separator());

        var exitItem = new MenuItem { Header = "종료" };
        exitItem.Click += (_, _) => Application.Current.Shutdown();
        menu.Items.Add(exitItem);

        return menu;
    }

    private void ToggleFlyout()
    {
        if (_flyout.IsVisible)
        {
            _flyout.Hide();
            return;
        }

        ShowFlyout();
    }

    private void ShowFlyout(bool keepOpenWhenDeactivated = false)
    {
        var trayPosition = TaskbarIcon.GetPopupTrayPosition();
        _flyout.ShowNear(
            new Point(trayPosition.X, trayPosition.Y),
            keepOpenWhenDeactivated);
    }

    private void ShowSettings()
    {
        if (_settingsWindow is { IsVisible: true })
        {
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(_settings, _settingsStore);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }
}
