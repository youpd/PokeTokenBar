using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using PokeTokenBar.App.Platform;
using PokeTokenBar.App.Tray;
using PokeTokenBar.Core.Companion;
using PokeTokenBar.Core.Limits;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;
using PokeTokenBar.Core.Poke;

namespace PokeTokenBar.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private SettingsStore? _settingsStore;
    private AppSettings? _settings;
    private LocalUsageCache? _usageCache;
    private UsageStore? _usageStore;
    private ClaudeLimitsProvider? _claudeLimitsProvider;
    private StatuspageProvider? _statusProvider;
    private PokeApiClient? _pokeApiClient;
    private SpriteStore? _spriteStore;
    private CompanionStore? _companionStore;
    private UsageRefreshCoordinator? _refreshCoordinator;
    private TrayController? _trayController;
    private bool _crashReporterInstalled;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var allowSecondaryForQa = false;
#if DEBUG
        allowSecondaryForQa = string.Equals(
            Environment.GetEnvironmentVariable("PTB_ALLOW_SECONDARY"),
            "1",
            StringComparison.Ordinal);
#endif
        if (!allowSecondaryForQa &&
            !SingleInstanceGuard.TryAcquire(
                SingleInstanceGuard.ApplicationMutexName,
                out _singleInstance))
        {
            Shutdown();
            return;
        }

        var version = Assembly.GetEntryAssembly()?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.1.0";

        CrashReporter.Install(version);
        _crashReporterInstalled = true;
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var paths = AppPaths.Default;
        paths.EnsureCreated();
        _settingsStore = new SettingsStore(paths.SettingsFile);
        _settings = _settingsStore.Load();

        AppLog.Write($"toast compatibility assembly: {ToastCompatibilityProbe.AssemblyVersion}");

        _usageCache = new LocalUsageCache(
            BuildUsageRoots(
                _settings,
                LocalUsageReader.DefaultClaudeProjectsDirectory,
                ".claude",
                "projects"),
            LocalUsageReader.ResolveCodexUsageDirectories(
                Environment.GetEnvironmentVariable("CODEX_HOME"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                _settings.ExtraHomes,
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            BuildUsageRoots(
                _settings,
                LocalUsageReader.DefaultGeminiTemporaryDirectory,
                ".gemini",
                "tmp"),
            paths.UsageCacheFile);
        _claudeLimitsProvider = new ClaudeLimitsProvider();
        _statusProvider = new StatuspageProvider();
        var codexLimitsProvider = new CodexRateLimitsProvider(
            version,
            () => _settings.CodexPath);
        _usageStore = new UsageStore(
            UsageProviderRegistry.CreateDefault(_usageCache),
            snapshotFile: paths.LastSnapshotFile,
            settings: _settings,
            claudeLimitsProvider: _claudeLimitsProvider,
            codexLimitsProvider: codexLimitsProvider,
            statusProvider: _statusProvider);

        _pokeApiClient = new PokeApiClient(paths.BaseIndexFile);
        _spriteStore = new SpriteStore(paths.SpritesDirectory);
        _companionStore = new CompanionStore(
            _pokeApiClient,
            stateFile: CompanionStore.DefaultStateFile());
        _usageStore.Changed += UsageStore_OnChangedForCompanion;

        _trayController = new TrayController(
            _settings,
            _settingsStore,
            _usageStore,
            _companionStore,
            _spriteStore);
        _refreshCoordinator = new UsageRefreshCoordinator(
            _usageStore,
            _settings,
            Dispatcher);
        _trayController.RefreshRequested = _refreshCoordinator.RefreshNowAsync;
        _trayController.SettingsChanged += (_, _) =>
        {
            _refreshCoordinator.ApplySettings();
            _ = _refreshCoordinator.RefreshNowAsync();
        };
        _trayController.Start();
        _refreshCoordinator.Start();

#if DEBUG
        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            _trayController.ShowFlyoutForSmokeTest();
            var smokeSeconds = int.TryParse(
                Environment.GetEnvironmentVariable("PTB_SMOKE_SECONDS"),
                out var configuredSmokeSeconds)
                ? Math.Clamp(configuredSmokeSeconds, 10, 120)
                : 15;
            var smokeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(smokeSeconds),
            };
            smokeTimer.Tick += (_, _) =>
            {
                smokeTimer.Stop();
                Shutdown();
            };
            smokeTimer.Start();
        }
#endif
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _refreshCoordinator?.Dispose();
        if (_usageStore is not null)
        {
            _usageStore.Changed -= UsageStore_OnChangedForCompanion;
        }
        _usageCache?.Flush();
        _trayController?.Dispose();
        _usageStore?.Dispose();
        _claudeLimitsProvider?.Dispose();
        _statusProvider?.Dispose();
        _companionStore?.Dispose();
        _spriteStore?.Dispose();
        _pokeApiClient?.Dispose();

        if (_settingsStore is not null && _settings is not null)
        {
            try
            {
                _settingsStore.Save(_settings);
            }
            catch (Exception exception)
            {
                AppLog.Write($"settings save during shutdown failed: {exception}");
            }
        }

        if (_crashReporterInstalled)
        {
            CrashReporter.MarkClean();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private static IReadOnlyList<string> BuildUsageRoots(
        AppSettings settings,
        string defaultRoot,
        params string[] relativeSegments)
    {
        var roots = new List<string>
        {
            defaultRoot,
        };

        foreach (var home in (settings.ExtraHomes ?? []).Where(home =>
                     !string.IsNullOrWhiteSpace(home)))
        {
            try
            {
                roots.Add(Path.Combine(
                    Environment.ExpandEnvironmentVariables(home),
                    Path.Combine(relativeSegments)));
            }
            catch (Exception exception) when (
                exception is ArgumentException or NotSupportedException)
            {
                AppLog.Write($"invalid extra home ignored: {exception.Message}");
            }
        }

        return roots;
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReporter.Report(e.Exception, "DispatcherUnhandledException");
        e.Handled = false;
    }

    private async void UsageStore_OnChangedForCompanion(object? sender, EventArgs e)
    {
        if (_usageStore is null || _companionStore is null)
        {
            return;
        }

        try
        {
            await _companionStore.UpdateAsync(
                _usageStore.TodayTotalTokens,
                _usageStore.TodayKey,
                _usageStore.BurnTier,
                _usageStore.IsLimitWarning,
                _usageStore.HasUsageData);
            _companionStore.GrantCandies(
                _usageStore.CandyEligibleWindows,
                _usageStore.LimitsReady);
        }
        catch (Exception exception)
        {
            AppLog.Write($"companion refresh failed: {exception}");
        }
    }
}
