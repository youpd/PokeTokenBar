using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using H.NotifyIcon;
using Microsoft.Toolkit.Uwp.Notifications;
using PokeTokenBar.App.Views;
using PokeTokenBar.Core.Companion;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Poke;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Tray;

internal sealed class TrayController : IDisposable
{
    private readonly AppSettings _settings;
    private readonly SettingsStore _settingsStore;
    private readonly UsageStore _usageStore;
    private readonly CompanionStore _companionStore;
    private readonly SpriteStore _spriteStore;
    private readonly FlyoutWindow _flyout;
    private readonly StackPanel _tooltipContent = new();
    private readonly TaskbarIcon _trayIcon;
    private readonly GeneratedIconSource _eggIcon = new()
    {
        Text = "🥚",
        FontFamily = new FontFamily("Segoe UI Emoji"),
        FontSize = 40,
        Background = Brushes.Transparent,
    };
    private readonly DispatcherTimer _animationTimer;
    private (string Key, GeneratedIconSource Low, GeneratedIconSource High)? _spriteFrames;
    private bool _highFrame;
    private SettingsWindow? _settingsWindow;

    public TrayController(
        AppSettings settings,
        SettingsStore settingsStore,
        UsageStore usageStore,
        CompanionStore companionStore,
        SpriteStore spriteStore)
    {
        _settings = settings;
        _settingsStore = settingsStore;
        _usageStore = usageStore;
        _companionStore = companionStore;
        _spriteStore = spriteStore;
        _flyout = new FlyoutWindow(companionStore, spriteStore);

        _trayIcon = new TaskbarIcon
        {
            ToolTipText = "PokeTokenBar — 알 · —",
            IconSource = _eggIcon,
            TrayToolTip = BuildCustomTooltip(),
            ContextMenu = BuildContextMenu(),
        };

        _trayIcon.TrayLeftMouseUp += (_, _) => ToggleFlyout();
        _usageStore.Changed += UsageStore_OnChanged;
        _usageStore.LimitAlertsRaised += UsageStore_OnLimitAlertsRaised;
        _companionStore.Changed += CompanionStore_OnChanged;
        _companionStore.CompanionEventRaised += CompanionStore_OnEvent;
        _flyout.RefreshRequested = RequestRefreshAsync;
        _flyout.ClaudeLimitsRefreshRequested = () =>
            _usageStore.RefreshClaudeLimitsFromCredentialAsync();
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _animationTimer.Tick += (_, _) =>
        {
            if (_spriteFrames is not { } frames) return;
            _highFrame = !_highFrame;
            _trayIcon.IconSource = _highFrame ? frames.High : frames.Low;
        };
    }

    public Func<Task>? RefreshRequested { get; set; }

    public event EventHandler? SettingsChanged;

    public void Start()
    {
        _trayIcon.ForceCreate();
        _animationTimer.Start();
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
        _usageStore.LimitAlertsRaised -= UsageStore_OnLimitAlertsRaised;
        _companionStore.Changed -= CompanionStore_OnChanged;
        _companionStore.CompanionEventRaised -= CompanionStore_OnEvent;
        _animationTimer.Stop();
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

        _settingsWindow = new SettingsWindow(
            _settings,
            _settingsStore,
            () => _usageStore.RefreshClaudeLimitsFromCredentialAsync());
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

    private void CompanionStore_OnChanged(object? sender, EventArgs e)
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

    private void CompanionStore_OnEvent(CompanionEvent companionEvent)
    {
        if (!_settings.CompanionNotifications || !AppEnv.IsRealApp)
        {
            return;
        }

        try
        {
            new ToastContentBuilder()
                .AddArgument("companion", companionEvent.Kind)
                .AddText(companionEvent.Title)
                .AddText(companionEvent.Body)
                .Show();
        }
        catch (Exception exception)
        {
            AppLog.Write($"companion toast failed: {exception.Message}");
        }
    }

    private static void UsageStore_OnLimitAlertsRaised(IReadOnlyList<LimitAlert> alerts)
    {
        foreach (var alert in alerts)
        {
            try
            {
                new ToastContentBuilder()
                    .AddArgument("limit", alert.Key)
                    .AddText(alert.IsCritical ? "한도 위험" : "한도 경고")
                    .AddText($"{alert.Name} {TokenFormatter.Percent(alert.Utilization)}")
                    .Show(toast => toast.Tag = alert.Key + (alert.IsCritical ? "-critical" : "-warning"));
            }
            catch (Exception exception)
            {
                AppLog.Write($"limit toast failed: {exception.Message}");
            }
        }
    }

    private void UpdatePresentation()
    {
        var companionName = _companionStore.IsEgg ? "알" : _companionStore.DisplayName;
        var header = $"PokeTokenBar — {companionName}";
        var lines = TrayText.TooltipLines(
            header,
            _usageStore.LastUpdated is not null,
            _settings.ShowTokensInMenu,
            _settings.ShowCostInMenu,
            _settings.ShowLimitInMenu,
            _usageStore.TodayTotalTokens,
            _usageStore.TodayCostTotal,
            limitLine: _usageStore.MenuLimitLine);

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
        _ = UpdateTrayIconAsync();
    }

    private async Task UpdateTrayIconAsync()
    {
        if (_companionStore.CurrentSpeciesID is not { } speciesId)
        {
            _spriteFrames = null;
            _trayIcon.IconSource = _eggIcon;
            return;
        }

        var key = $"{speciesId}:{_companionStore.CurrentIsShiny}";
        if (_spriteFrames is { } existing && existing.Key == key)
        {
            return;
        }

        var data = await _spriteStore.GetSpeciesAsync(
            speciesId,
            animated: false,
            _companionStore.CurrentIsShiny);
        if (data is null)
        {
            return;
        }

        var cachedPath = _spriteStore.FindCachedSpeciesPath(
            speciesId,
            animated: false,
            _companionStore.CurrentIsShiny);
        if (cachedPath is null)
        {
            return;
        }

        var image = new BitmapImage(new Uri(cachedPath, UriKind.Absolute));
        image.Freeze();
        var low = new GeneratedIconSource
        {
            Text = string.Empty,
            Background = Brushes.Transparent,
            BackgroundSource = image,
            Margin = new Thickness(5, 5, 5, 2),
        };
        var high = new GeneratedIconSource
        {
            Text = string.Empty,
            Background = Brushes.Transparent,
            BackgroundSource = image,
            Margin = new Thickness(5, 2, 5, 5),
        };
        _spriteFrames = (key, low, high);
        _trayIcon.IconSource = low;
    }
}
