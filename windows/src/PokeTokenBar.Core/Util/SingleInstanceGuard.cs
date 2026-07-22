namespace PokeTokenBar.Core.Util;

public sealed class SingleInstanceGuard : IDisposable
{
    public const string ApplicationMutexName = @"Global\PokeTokenBar";

    private Mutex? _mutex;
    private bool _ownsMutex;

    private SingleInstanceGuard(Mutex mutex)
    {
        _mutex = mutex;
        _ownsMutex = true;
    }

    public static bool TryAcquire(string mutexName, out SingleInstanceGuard? guard)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        guard = null;

        try
        {
            var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
            if (!createdNew)
            {
                mutex.Dispose();
                return false;
            }

            guard = new SingleInstanceGuard(mutex);
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        var mutex = Interlocked.Exchange(ref _mutex, null);
        if (mutex is null)
        {
            return;
        }

        if (_ownsMutex)
        {
            try
            {
                mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // The mutex is no longer owned by this thread.
            }

            _ownsMutex = false;
        }

        mutex.Dispose();
    }
}
