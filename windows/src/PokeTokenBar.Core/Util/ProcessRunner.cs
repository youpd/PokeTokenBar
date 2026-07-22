using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PokeTokenBar.Core.Util;

public sealed class ProcessRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public async Task<JsonElement> RunCodexRateLimitsAsync(
        string binaryPath,
        string appVersion,
        CancellationToken cancellationToken = default)
    {
        var startInfo = BuildCodexStartInfo(binaryPath);
        var stdoutPath = Path.Combine(Path.GetTempPath(), $"ptb-codex-{Guid.NewGuid():N}.out");
        var stderrPath = Path.Combine(Path.GetTempPath(), $"ptb-codex-{Guid.NewGuid():N}.err");
        try
        {
            using var process = new Process { StartInfo = startInfo };
            process.Start();
            await using var stdoutFile = new FileStream(
                stdoutPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite,
                4096,
                useAsync: true);
            await using var stderrFile = new FileStream(
                stderrPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite,
                4096,
                useAsync: true);
            var stdoutPump = process.StandardOutput.BaseStream.CopyToAsync(
                stdoutFile,
                cancellationToken);
            var stderrPump = process.StandardError.BaseStream.CopyToAsync(
                stderrFile,
                cancellationToken);

            var payload = string.Join('\n',
                JsonSerializer.Serialize(new
                {
                    method = "initialize",
                    id = 0,
                    @params = new
                    {
                        clientInfo = new
                        {
                            name = "token_win",
                            title = "PokeTokenBar",
                            version = appVersion,
                        },
                        capabilities = new { experimentalApi = true },
                    },
                }),
                JsonSerializer.Serialize(new
                {
                    method = "initialized",
                    @params = new { },
                }),
                JsonSerializer.Serialize(new
                {
                    method = "account/rateLimits/read",
                    id = 1,
                    @params = new { },
                })) + "\n";
            var bytes = Utf8NoBom.GetBytes(payload);
            await process.StandardInput.BaseStream.WriteAsync(bytes, cancellationToken)
                .ConfigureAwait(false);
            await process.StandardInput.BaseStream.FlushAsync(cancellationToken)
                .ConfigureAwait(false);

            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken)
                    .ConfigureAwait(false);
                await stdoutFile.FlushAsync(cancellationToken).ConfigureAwait(false);
                var result = TryReadResponse(stdoutPath);
                if (result is { } response)
                {
                    KillTree(process);
                    await AwaitPumps(stdoutPump, stderrPump).ConfigureAwait(false);
                    return response;
                }

                if (process.HasExited)
                {
                    await AwaitPumps(stdoutPump, stderrPump).ConfigureAwait(false);
                    throw new InvalidOperationException(
                        $"codex app-server exited before responding: {ReadTail(stderrPath, 300)}");
                }
            }

            KillTree(process);
            await AwaitPumps(stdoutPump, stderrPump).ConfigureAwait(false);
            throw new TimeoutException(
                $"codex app-server timed out: {ReadTail(stderrPath, 300)}");
        }
        finally
        {
            TryDelete(stdoutPath);
            TryDelete(stderrPath);
        }
    }

    public static ProcessStartInfo BuildCodexStartInfo(string binaryPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(binaryPath);
        var fullPath = Path.GetFullPath(binaryPath);
        var extension = Path.GetExtension(fullPath);
        if (extension.Equals(".ps1", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException("PowerShell shims are not supported for background execution.");
        }

        var startInfo = new ProcessStartInfo
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8NoBom,
            StandardOutputEncoding = Utf8NoBom,
            StandardErrorEncoding = Utf8NoBom,
        };
        if (extension.Equals(".cmd", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".bat", StringComparison.OrdinalIgnoreCase))
        {
            startInfo.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
            startInfo.Arguments = $"/d /s /c \"\"{fullPath}\" app-server --stdio\"";
        }
        else
        {
            startInfo.FileName = fullPath;
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--stdio");
        }

        var inheritedPath = startInfo.Environment.TryGetValue("PATH", out var path)
            ? path
            : Environment.GetEnvironmentVariable("PATH");
        startInfo.Environment["PATH"] = string.Join(
            Path.PathSeparator,
            BinaryLocator.CommonToolDirectories().Concat(
                string.IsNullOrWhiteSpace(inheritedPath) ? [] : [inheritedPath]));
        return startInfo;
    }

    public static JsonElement? TryReadResponse(string stdoutPath)
    {
        try
        {
            using var stream = new FileStream(
                stdoutPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            using var reader = new StreamReader(stream, Utf8NoBom);
            while (reader.ReadLine() is { } line)
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    using var json = JsonDocument.Parse(line);
                    var root = json.RootElement;
                    if (!root.TryGetProperty("id", out var id) || id.GetInt32() != 1)
                    {
                        continue;
                    }

                    if (root.TryGetProperty("error", out var error))
                    {
                        throw new InvalidOperationException(
                            $"codex app-server error: {error.GetRawText()}");
                    }

                    if (root.TryGetProperty("result", out var result))
                    {
                        return result.Clone();
                    }
                }
                catch (JsonException)
                {
                    // stdout may contain diagnostics or notifications; scan the next line.
                }
            }
        }
        catch (IOException)
        {
            // The writer may be between line flushes; retry at the next poll.
        }

        return null;
    }

    private static async Task AwaitPumps(params Task[] pumps)
    {
        try
        {
            await Task.WhenAll(pumps).WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is IOException or OperationCanceledException or TimeoutException)
        {
            // The process tree was intentionally terminated after its one response.
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
            // It exited between checks.
        }
    }

    private static string ReadTail(string path, int maxCharacters)
    {
        try
        {
            var text = File.ReadAllText(path);
            return text.Length <= maxCharacters ? text : text[^maxCharacters..];
        }
        catch (IOException)
        {
            return string.Empty;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Temp cleanup is best effort.
        }
    }
}
