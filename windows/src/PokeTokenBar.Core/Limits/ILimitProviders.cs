using PokeTokenBar.Core.Models;

namespace PokeTokenBar.Core.Limits;

public interface IClaudeLimitsProvider
{
    Task<ClaudeLimitStatus> FetchAsync(
        bool forceCredentialReload = false,
        CancellationToken cancellationToken = default);
}

public interface ICodexLimitsProvider
{
    Task<CodexRateLimitsResult?> FetchAsync(CancellationToken cancellationToken = default);
}

public interface IProviderStatusProvider
{
    Task<IReadOnlyDictionary<string, ProviderStatus>> FetchAsync(
        CancellationToken cancellationToken = default);
}

public sealed class ClaudeLimitsException : Exception
{
    public ClaudeLimitsException(
        string message,
        bool isAuthenticationExpired = false,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsAuthenticationExpired = isAuthenticationExpired;
        RetryAfter = retryAfter;
    }

    public bool IsAuthenticationExpired { get; }

    public TimeSpan? RetryAfter { get; }
}
