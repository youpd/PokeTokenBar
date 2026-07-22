using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using H.NotifyIcon;
using PokeTokenBar.App.Views;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Tray;

internal sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly UsageStore _usageStore;
    private readonly FlyoutWindow _flyout = new();
    private readonly StackPanel _tooltipContent = new();
    private readonly TaskbarIcon _trayIcon;
    private SettingsWindow? _settingsWindow;

    public TrayController(
        AppSettings settings,
        SettingsStore settingsStore,
        UsageStore usageStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _usageStore = usageStore;

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "PokeTokenBar — 알 · —",
            IconSource = new GeneratedIconSource
            {
                Text = "🥚",
                FontFamily = new FontFamily("Segoe UI Emoji"),
                FontSize = 40,
                Background = Brushes.Transparent,
            },
            TrayToolTip = BuildCustomTooltip(),
            ContextMenu = BuildContextMenu(),
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => ToggleFlyout();
        _usageStore.Changed += UsageStore_OnChanged;
        _flyout.RefreshRequested = RequestRefreshAsync;
    }

    public Func<Task>? RefreshRequested { get; set; }

    public event EventHandler? SettingsChanged;

    public void Start()
    {
        _trayIcon.ForceCreate();
        UpdatePresentation();
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
        _usageStore.Changed -= UsageStore_OnChanged;
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
        refreshItem.Click += async (_, _) => await RequestRefreshAsync();
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
        _flyout.UpdateDisplay(_usageStore);
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
        _settingsWindow.Saved += (_, _) =>
        {
            UpdatePresentation();
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        };
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private FrameworkElement BuildCustomTooltip()
    {
        return new Border
        {
            Padding = new Thickness(12, 9, 12, 9),
            Background = new SolidColorBrush(Color.FromRgb(35, 38, 45)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(64, 74, 90)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = _tooltipContent,
        };
    }

    private async Task RequestRefreshAsync()
    {
        if (RefreshRequested is null)
        {
            return;
        }

        AppLog.Write("manual usage refresh requested");
        try
        {
            await RefreshRequested();
        }
        catch (Exception exception)
        {
            AppLog.Write($"manual usage refresh failed: {exception}");
        }
    }

    private void UsageStore_OnChanged(object? sender, EventArgs e)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess())
        {
            UpdatePresentation();
        }
        else
        {
            dispatcher.BeginInvoke(UpdatePresentation);
        }
    }

    private void UpdatePresentation()
    {
        const string header = "PokeTokenBar — 알";
        var lines = TrayText.TooltipLines(
            header,
            _usageStore.LastUpdated is not null,
            _settings.ShowTokensInMenu,
            _settings.ShowCostInMenu,
            _settings.ShowLimitInMenu,
            _usageStore.TodayTotalTokens,
            _usageStore.TodayCostTotal,
            limitLine: null);

        _trayIcon.ToolTipText = TrayText.FallbackText(lines);
        _tooltipContent.Children.Clear();
        for (var index = 0; index < lines.Count; index++)
        {
            _tooltipContent.Children.Add(new TextBlock
            {
                Margin = index == 0 ? new Thickness(0) : new Thickness(0, 4, 0, 0),
                FontWeight = index == 0 ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = index == 0
                    ? Brushes.White
                    : new SolidColorBrush(Color.FromRgb(183, 189, 200)),
                Text = lines[index],
            });
        }

        _flyout.UpdateDisplay(_usageStore);
    }
}
