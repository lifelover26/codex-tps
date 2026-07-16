using System;
using System.Threading;

namespace CodexTPSTray;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex? _mutex;
    private bool _isDisposed;

    public bool IsOwned { get; }

    public SingleInstanceGuard(string mutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, mutexName, out bool createdNew);
        IsOwned = createdNew;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _isDisposed = true;

        if (_mutex != null)
        {
            if (IsOwned)
            {
                try
                {
                    _mutex.ReleaseMutex();
                }
                catch
                {
                }
            }

            _mutex.Dispose();
        }
    }
}