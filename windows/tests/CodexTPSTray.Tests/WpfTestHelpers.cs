using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;

namespace CodexTPSTray.Tests;

internal static class WpfTestHelpers
{
    // Serializes every helper invocation across the whole test process so that
    // PanelThemeResourcesTests, OverlayThemeResourcesTests and
    // TrayIconManagerDataSourceTests can never touch the shared Application
    // concurrently, even when xUnit runs different collections in parallel.
    private static readonly SemaphoreSlim _serializationGate = new(1, 1);

    private static readonly object _hostInitLock = new();
    private static volatile Task? _hostReadyTask;
    private static Dispatcher? _hostDispatcher;

    // Returns the shared STA host Dispatcher after EnsureHostReadyAsync has
    // completed. A null dispatcher here means initialization failed or has not
    // run yet; surface that as a clear, actionable error instead of an NRE.
    private static Dispatcher HostDispatcher =>
        _hostDispatcher
        ?? throw new InvalidOperationException(
            "WPF host Dispatcher is not initialized. " +
            "Ensure EnsureHostReadyAsync has completed before using the host.");

    public static void RunInStaWithWpf(Action action)
    {
        EnsureHostReadyAsync().GetAwaiter().GetResult();
        _serializationGate.Wait();
        try
        {
            // Dispatcher.Invoke executes on the shared STA host and re-throws
            // any exception on the calling thread.
            HostDispatcher.Invoke(action);
        }
        finally
        {
            _serializationGate.Release();
        }
    }

    public static async Task RunInStaWithWpfAsync(Func<Task> asyncAction)
    {
        await EnsureHostReadyAsync().ConfigureAwait(false);
        await _serializationGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Dispatcher.InvokeAsync posts asyncAction onto the shared STA host
            // Dispatcher. Because DispatcherSynchronizationContext is installed
            // on that thread, continuations raised by `await asyncAction()` are
            // marshaled back to the same STA Dispatcher and pumped by
            // Dispatcher.Run(). We never use async void and we never ignore the
            // DispatcherOperation return value. asyncAction starts and continues
            // on the host STA Dispatcher; only the outer wait uses
            // ConfigureAwait(false).
            DispatcherOperation<Task> operation = HostDispatcher.InvokeAsync(asyncAction);
            await operation.Task.Unwrap().ConfigureAwait(false);
        }
        finally
        {
            _serializationGate.Release();
        }
    }

    private static Task EnsureHostReadyAsync()
    {
        if (_hostReadyTask != null)
            return _hostReadyTask;

        lock (_hostInitLock)
        {
            if (_hostReadyTask != null)
                return _hostReadyTask;

            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    var app = WpfApplication.Current;
                    if (app == null)
                    {
                        app = new WpfApplication();
                        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    }

                    Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
                    _hostDispatcher = dispatcher;
                    // Install the synchronization context BEFORE Dispatcher.Run
                    // so async continuations inside test lambdas are marshaled
                    // back to this STA Dispatcher.
                    SynchronizationContext.SetSynchronizationContext(
                        new DispatcherSynchronizationContext(dispatcher));
                    ready.TrySetResult();
                    Dispatcher.Run();
                }
                catch (Exception ex)
                {
                    ready.TrySetException(ex);
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.IsBackground = true;
            // Assign _hostReadyTask BEFORE starting the thread to avoid an init
            // race where a concurrent caller observes a null _hostReadyTask
            // after the thread has already begun initializing the host.
            _hostReadyTask = ready.Task;
            thread.Start();
            return _hostReadyTask;
        }
    }
}
