using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using PokeTokenBar.App.Platform;
using PokeTokenBar.App.Tray;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Usage;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private SettingsStore? _settingsStore;
    private AppSettings? _settings;
    private LocalUsageCache? _usageCache;
    private UsageStore? _usageStore;
    private UsageRefreshCoordinator? _refreshCoordinator;
    private TrayController? _trayController;
    private bool _crashReporterInstalled;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (!SingleInstanceGuard.TryAcquire(
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
        _usageStore = new UsageStore(
            UsageProviderRegistry.CreateDefault(_usageCache),
            snapshotFile: paths.LastSnapshotFile);

        _trayController = new TrayController(_settings, _settingsStore, _usageStore);
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
            var smokeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(15),
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
        _usageCache?.Flush();
        _trayController?.Dispose();
        _usageStore?.Dispose();

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
}
