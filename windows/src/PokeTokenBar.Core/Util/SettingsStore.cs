using System.Text.Json;
using System.Text.Json.Serialization;
using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Util;

public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true,
    };

    private readonly string _filePath;

    public SettingsStore(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = Path.GetFullPath(filePath);
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            using var stream = new FileStream(
                _filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);

            return JsonSerializer.Deserialize<AppSettings>(stream, JsonOptions) ?? new AppSettings();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write($"settings load failed; defaults will be used: {exception.Message}");
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("Settings path has no parent directory.");
        Directory.CreateDirectory(directory);

        var temporaryPath = _filePath + ".tmp";
        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                JsonSerializer.Serialize(stream, settings, JsonOptions);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
