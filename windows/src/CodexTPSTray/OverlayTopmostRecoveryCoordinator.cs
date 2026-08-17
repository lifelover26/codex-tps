using System;
using System.Threading;
using System.Windows.Threading;

namespace CodexTPSTray;

internal interface IOverlayTopmostRecoveryTarget
{
    bool IsVisible { get; }
    bool IsEnabled { get; }
    void ReassertTopmost();
    void ReconcilePlacement();
}

internal interface IRecoveryTimer
{
    void Restart(TimeSpan delay, Action callback);
    void Cancel();
}

internal sealed class DispatcherRecoveryTimer : IRecoveryTimer
{
    private DispatcherTimer? _timer;
    private Action? _callback;

    public void Restart(TimeSpan delay, Action callback)
    {
        Cancel();
        _callback = callback;
        _timer = new DispatcherTimer { Interval = delay };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Cancel()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
        _callback = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var cb = _callback;
        Cancel();
        cb?.Invoke();
    }
}

internal sealed class OverlayTopmostRecoveryCoordinator : IDisposable
{
    private readonly IOverlayRecoveryEventSource _source;
    private readonly IDispatcher _dispatcher;
    private readonly IOverlayTopmostRecoveryTarget _target;
    private readonly IRecoveryTimer _timer;
    private readonly TimeSpan _delay;
    private readonly object _lifecycleLock = new();
    private int _immediatePending;
    private bool _isShuttingDown;
    private bool _isDisposed;
    private bool _isStarted;

    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(1500);

    public OverlayTopmostRecoveryCoordinator(
        IOverlayRecoveryEventSource source,
        IDispatcher dispatcher,
        IOverlayTopmostRecoveryTarget target)
        : this(source, dispatcher, target, new DispatcherRecoveryTimer(), DefaultRetryDelay)
    {
    }

    internal OverlayTopmostRecoveryCoordinator(
        IOverlayRecoveryEventSource source,
        IDispatcher dispatcher,
        IOverlayTopmostRecoveryTarget target,
        IRecoveryTimer timer,
        TimeSpan delay)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _delay = delay;
    }

    public bool Start()
    {
        lock (_lifecycleLock)
        {
            if (_isDisposed || _isShuttingDown)
                return false;

            if (_isStarted)
                return true;

            bool result = _source.Subscribe(OnRecoveryEvent);
            if (result)
            {
                _isStarted = true;
            }
            return result;
        }
    }

    public void PrepareForShutdown()
    {
        lock (_lifecycleLock)
        {
            if (_isShuttingDown)
                return;

            _isShuttingDown = true;

            if (_isStarted)
            {
                _source.Unsubscribe();
                _isStarted = false;
            }
        }

        _timer.Cancel();
    }

    public void Dispose()
    {
        lock (_lifecycleLock)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;
            _isShuttingDown = true;

            if (_isStarted)
            {
                _source.Unsubscribe();
                _isStarted = false;
            }
        }

        _timer.Cancel();
    }

    private void OnRecoveryEvent()
    {
        if (Volatile.Read(ref _isShuttingDown) || Volatile.Read(ref _isDisposed))
            return;

        if (Interlocked.CompareExchange(ref _immediatePending, 1, 0) != 0)
            return;

        try
        {
            _dispatcher.BeginInvoke(OnImmediateDispatch);
        }
        catch
        {
            Interlocked.Exchange(ref _immediatePending, 0);
        }
    }

    private void OnImmediateDispatch()
    {
        try
        {
            if (Volatile.Read(ref _isShuttingDown) || Volatile.Read(ref _isDisposed))
                return;

            if (_target.IsEnabled && _target.IsVisible)
            {
                _target.ReassertTopmost();
                _target.ReconcilePlacement();
            }

            _timer.Restart(_delay, OnDelayedRetry);
        }
        finally
        {
            Interlocked.Exchange(ref _immediatePending, 0);
        }
    }

    private void OnDelayedRetry()
    {
        if (Volatile.Read(ref _isShuttingDown) || Volatile.Read(ref _isDisposed))
            return;

        if (_target.IsEnabled && _target.IsVisible)
        {
            _target.ReassertTopmost();
            _target.ReconcilePlacement();
        }
    }
}
