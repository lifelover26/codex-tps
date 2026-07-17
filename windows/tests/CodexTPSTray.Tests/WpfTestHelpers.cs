using System;
using System.Threading;

namespace CodexTPSTray.Tests;

internal static class WpfTestHelpers
{
    private static readonly object _lock = new();
    private static bool _initialized;

    public static void RunInStaWithWpf(Action action)
    {
        if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA && System.Windows.Application.Current != null)
        {
            action();
            return;
        }

        Exception? exception = null;
        var thread = new Thread(() =>
        {
            try
            {
                InitializeWpfOnce();
                action();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (exception != null)
        {
            throw exception;
        }
    }

    private static void InitializeWpfOnce()
    {
        lock (_lock)
        {
            if (!_initialized && System.Windows.Application.Current == null)
            {
                _ = new System.Windows.Application();
                _initialized = true;
            }
        }
    }
}
