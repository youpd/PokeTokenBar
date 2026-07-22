using PokeTokenBar.Core.Util;

namespace PokeTokenBar.Tests;

public sealed class SingleInstanceGuardTests
{
    [Fact]
    public void SecondGuardCannotAcquireSameNamedMutex()
    {
        var name = $@"Local\PokeTokenBar.Tests.{Guid.NewGuid():N}";

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));
        Assert.NotNull(first);
        try
        {
            Assert.False(SingleInstanceGuard.TryAcquire(name, out var second));
            Assert.Null(second);
        }
        finally
        {
            first.Dispose();
        }
    }

    [Fact]
    public void GuardCanBeAcquiredAgainAfterDispose()
    {
        var name = $@"Local\PokeTokenBar.Tests.{Guid.NewGuid():N}";
        Assert.True(SingleInstanceGuard.TryAcquire(name, out var first));
        first!.Dispose();

        Assert.True(SingleInstanceGuard.TryAcquire(name, out var second));
        second!.Dispose();
    }
}
