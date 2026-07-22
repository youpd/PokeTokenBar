using System.Windows.Threading;
using Microsoft.Win32;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App.Platform;

internal sealed class UsageRefreshCoordinator : IDisposable
{
    private readonly UsageStore _store;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _midnightTimer;
    private readonly CancellationTokenSource _shutdown = new();
    private bool _pollingSuspended;
    private bool _started;
    private bool _disposed;

    public UsageRefreshCoordinator(
        UsageStore store,
        AppSettings settings,
        Dispatcher dispatcher)
    {
        _store = store;
        _settings = settings;
        _dispatcher = dispatcher;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _refreshTimer.Tick += RefreshTimer_OnTick;
        _midnightTimer = new DispatcherTimer(DispatcherPriority.Background, dispatcher);
        _midnightTimer.Tick += MidnightTimer_OnTick;
    }

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        SystemEvents.PowerModeChanged += SystemEvents_OnPowerModeChanged;
        SystemEvents.SessionSwitch += SystemEvents_OnSessionSwitch;
        ApplySettings();
        ScheduleMidnightRefresh();
        _ = RefreshNowAsync();
    }

    public void ApplySettings()
    {
        _refreshTimer.Stop();
        if (!_pollingSuspended && _settings.RefreshInterval > 0)
        {
            _refreshTimer.Interval = TimeSpan.FromSeconds(
                Math.Max(1, _settings.RefreshInterval));
            _refreshTimer.Start();
        }
    }

    public async Task RefreshNowAsync()
    {
        if (_disposed || _pollingSuspended)
        {
            return;
        }

        try
        {
            await _store.RefreshAsync(
                scheduleEmptyRetry: true,
                _shutdown.Token);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            // Normal application shutdown.
        }
        catch (Exception exception)
        {
            AppLog.Write($"usage refresh coordinator failed: {exception}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _refreshTimer.Stop();
        _midnightTimer.Stop();
        if (_started)
        {
            SystemEvents.PowerModeChanged -= SystemEvents_OnPowerModeChanged;
            SystemEvents.SessionSwitch -= SystemEvents_OnSessionSwitch;
        }

        _shutdown.Dispose();
    }

    private async void RefreshTimer_OnTick(object? sender, EventArgs e) =>
        await RefreshNowAsync();

    private async void MidnightTimer_OnTick(object? sender, EventArgs e)
    {
        _midnightTimer.Stop();
        await RefreshNowAsync();
        ScheduleMidnightRefresh();
    }

    private void ScheduleMidnightRefresh()
    {
        _midnightTimer.Stop();
        if (_pollingSuspended)
        {
            return;
        }

        var now = DateTime.Now;
        var nextDay = now.Date.AddDays(1).AddSeconds(1);
        _midnightTimer.Interval = nextDay - now;
        _midnightTimer.Start();
    }

    private void SystemEvents_OnPowerModeChanged(
        object sender,
        PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
        {
            _dispatcher.BeginInvoke(SuspendPolling);
        }
        else if (e.Mode == PowerModes.Resume)
        {
            _dispatcher.BeginInvoke(ResumePolling);
        }
    }

    private void SystemEvents_OnSessionSwitch(
        object sender,
        SessionSwitchEventArgs e)
    {
        if (e.Reason == SessionSwitchReason.SessionLock)
        {
            _dispatcher.BeginInvoke(SuspendPolling);
        }
        else if (e.Reason == SessionSwitchReason.SessionUnlock)
        {
            _dispatcher.BeginInvoke(ResumePolling);
        }
    }

    private void SuspendPolling()
    {
        if (_disposed)
        {
            return;
        }

        _pollingSuspended = true;
        _refreshTimer.Stop();
        _midnightTimer.Stop();
        AppLog.Write("usage polling suspended");
    }

    private void ResumePolling()
    {
        if (_disposed || !_pollingSuspended)
        {
            return;
        }

        _pollingSuspended = false;
        ApplySettings();
        ScheduleMidnightRefresh();
        AppLog.Write("usage polling resumed");
        _ = RefreshNowAsync();
    }
}
