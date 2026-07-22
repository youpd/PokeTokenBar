namespace PokeTokenBar.Core.Util;

public static class AppLog
{
    private const long MaxBytes = 2 * 1024 * 1024;
    private static readonly Lazy<RollingFileLog> Log = new(() => new RollingFileLog(
        AppPaths.Default.LogFile,
        AppPaths.Default.OldLogFile,
        MaxBytes));

    public static string LogFilePath => AppPaths.Default.LogFile;

    public static void Write(string message)
    {
        if (!AppEnv.IsRealApp)
        {
            return;
        }

        try
        {
            Log.Value.Write(message);
        }
        catch
        {
            // Logging must never crash the tray application.
        }
    }
}
