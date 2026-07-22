using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Tests;

public sealed class RollingFileLogTests
{
    [Fact]
    public void OversizedLogRotatesToOneOldGeneration()
    {
        using var temporary = new TemporaryDirectory();
        var filePath = Path.Combine(temporary.Path, "logs", "app.log");
        var oldFilePath = Path.Combine(temporary.Path, "logs", "app.old.log");
        var log = new RollingFileLog(filePath, oldFilePath, maxBytes: 32);

        log.Write(new string('a', 64));
        log.Write("second");

        Assert.True(File.Exists(filePath));
        Assert.True(File.Exists(oldFilePath));
        Assert.Contains("second", File.ReadAllText(filePath), StringComparison.Ordinal);
        Assert.Contains(new string('a', 64), File.ReadAllText(oldFilePath), StringComparison.Ordinal);
    }
}
