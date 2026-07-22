using System.Globalization;
using System.Text;

namespace PokeTokenBar.Core.Util;

public sealed class RollingFileLog
{
    private readonly Lock _gate = new();
    private readonly string _filePath;
    private readonly string _oldFilePath;
    private readonly long _maxBytes;

    public RollingFileLog(string filePath, string oldFilePath, long maxBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(oldFilePath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);

        _filePath = Path.GetFullPath(filePath);
        _oldFilePath = Path.GetFullPath(oldFilePath);
        _maxBytes = maxBytes;
    }

    public void Write(string message)
    {
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"[{DateTimeOffset.UtcNow:O}] {message}{Environment.NewLine}");

        lock (_gate)
        {
            var directory = Path.GetDirectoryName(_filePath)
                ?? throw new InvalidOperationException("Log path has no parent directory.");
            Directory.CreateDirectory(directory);

            if (File.Exists(_filePath) && new FileInfo(_filePath).Length > _maxBytes)
            {
                File.Move(_filePath, _oldFilePath, overwrite: true);
            }

            File.AppendAllText(_filePath, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
    }
}
