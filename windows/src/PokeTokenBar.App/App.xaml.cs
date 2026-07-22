using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using PokeTokenBar.App.Platform;
using PokeTokenBar.App.Tray;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.App;

public partial class App : Application
{
    private SingleInstanceGuard? _singleInstance;
    private SettingsStore? _settingsStore;
    private AppSettings? _settings;
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

        _trayController = new TrayController(_settings, _settingsStore);
        _trayController.Start();

#if DEBUG
        if (e.Args.Contains("--smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            _trayController.ShowFlyoutForSmokeTest();
            var smokeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5),
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
        _trayController?.Dispose();

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

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashReporter.Report(e.Exception, "DispatcherUnhandledException");
        e.Handled = false;
    }
}
