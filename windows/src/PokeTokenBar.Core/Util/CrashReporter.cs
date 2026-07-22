namespace PokeTokenBar.Core.Util;

public static class CrashReporter
{
    private static int _installed;
    private static int _hasUnhandledException;

    public static bool HasUnhandledException => Volatile.Read(ref _hasUnhandledException) != 0;

    public static void Install(string version)
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
        {
            return;
        }

        AppPaths.Default.EnsureCreated();

        if (File.Exists(AppPaths.Default.RunningMarkerFile))
        {
            AppLog.Write("previous session did not shut down cleanly (crash, forced exit, or power loss)");
        }

        File.WriteAllText(AppPaths.Default.RunningMarkerFile, DateTimeOffset.UtcNow.ToString("O"));
        AppLog.Write($"launch: PokeTokenBar {version}");

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception
                ?? new InvalidOperationException($"Unhandled non-Exception object: {args.ExceptionObject}");
            Report(exception, "AppDomain.UnhandledException");
        };

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Report(args.Exception, "TaskScheduler.UnobservedTaskException");
        };
    }

    public static void Report(Exception exception, string source)
    {
        Interlocked.Exchange(ref _hasUnhandledException, 1);
        AppLog.Write($"CRASH [{source}]{Environment.NewLine}{exception}");
    }

    public static void MarkClean()
    {
        if (HasUnhandledException)
        {
            return;
        }

        try
        {
            File.Delete(AppPaths.Default.RunningMarkerFile);
        }
        catch (IOException exception)
        {
            AppLog.Write($"clean-shutdown marker removal failed: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            AppLog.Write($"clean-shutdown marker removal failed: {exception.Message}");
        }

        AppLog.Write("clean shutdown");
    }
}
