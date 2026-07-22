using System.Text.Json;
using PokeTokenBar.Core.Models;
using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Core.Limits;

public sealed class CodexRateLimitsProvider : ICodexLimitsProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly BinaryLocator _binaryLocator;
    private readonly ProcessRunner _processRunner;
    private readonly Func<string?> _manualPath;
    private readonly string _appVersion;

    public CodexRateLimitsProvider(
        string appVersion,
        Func<string?>? manualPath = null,
        BinaryLocator? binaryLocator = null,
        ProcessRunner? processRunner = null)
    {
        _appVersion = appVersion;
        _manualPath = manualPath ?? (() => null);
        _binaryLocator = binaryLocator ?? new BinaryLocator();
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public async Task<CodexRateLimitsResult?> FetchAsync(
        CancellationToken cancellationToken = default)
    {
        var binary = _binaryLocator.LocateCodex(_manualPath());
        if (binary is null)
        {
            return null;
        }

        var result = await _processRunner.RunCodexRateLimitsAsync(
                binary,
                _appVersion,
                cancellationToken)
            .ConfigureAwait(false);
        return result.Deserialize<CodexRateLimitsResult>(JsonOptions)
            ?? throw new JsonException("Codex rate limit response was empty.");
    }
}
