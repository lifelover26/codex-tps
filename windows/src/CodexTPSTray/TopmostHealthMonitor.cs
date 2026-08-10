using System;
using System.Threading;
using System.Windows.Threading;

namespace CodexTPSTray;

/// <summary>
/// Probe and recovery surface used by <see cref="TopmostHealthMonitor"/>. The
/// monitor reads the native WS_EX_TOPMOST bit through this target and only
/// calls <see cref="RecoverTopmost"/> when that bit is missing, so recovery is
/// conditional rather than unconditional.
/// </summary>
internal interface ITopmostHealthTarget
{
    bool IsVisible { get; }
    bool IsEnabled { get; }
    bool IsNativeTopmostSet();
    void RecoverTopmost();
}

/// <summary>
/// Recurring timer abstraction for the topmost health check. Implemented by
/// <see cref="DispatcherTopmostHealthTimer"/> in production and faked in tests.
/// </summary>
internal interface ITopmostHealthTimer
{
    void Start(TimeSpan interval, Action tick);
    void Stop();
}

internal sealed class DispatcherTopmostHealthTimer : ITopmostHealthTimer
{
    private DispatcherTimer? _timer;
    private Action? _tick;

    public void Start(TimeSpan interval, Action tick)
    {
        Stop();
        _tick = tick;
        _timer = new DispatcherTimer { Interval = interval };
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTick;
            _timer = null;
        }
        _tick = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _tick?.Invoke();
    }
}

/// <summary>
/// Lightweight topmost health monitor. While the overlay is visible and enabled,
/// a low-frequency timer (default 2s) and coalesced WM_WINDOWPOSCHANGED
/// notifications trigger a single <see cref="CheckOnce"/>. The check reads the
/// native WS_EX_TOPMOST bit via one GetWindowLong call; if the bit is present no
/// SetWindowPos is issued, and if it is missing <see cref="ITopmostHealthTarget.RecoverTopmost"/>
/// is invoked exactly once. Hiding or shutting down stops the timer so no
/// background polling continues while the overlay is off.
/// </summary>
internal sealed class TopmostHealthMonitor : IDisposable
{
    private readonly ITopmostHealthTarget _target;
    private readonly IDispatcher _dispatcher;
    private readonly ITopmostHealthTimer _timer;
    private readonly TimeSpan _interval;
    private readonly object _lock = new();
    private int _checkPending;
    private bool _isRunning;
    private bool _isShuttingDown;
    private bool _isDisposed;

    private static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(2);

    public TopmostHealthMonitor(ITopmostHealthTarget target, IDispatcher dispatcher)
        : this(target, dispatcher, new DispatcherTopmostHealthTimer(), DefaultInterval)
    {
    }

    internal TopmostHealthMonitor(
        ITopmostHealthTarget target,
        IDispatcher dispatcher,
        ITopmostHealthTimer timer,
        TimeSpan interval)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _timer = timer ?? throw new ArgumentNullException(nameof(timer));
        _interval = interval;
    }

    /// <summary>
    /// Starts the periodic timer and queues an immediate check. Idempotent.
    /// </summary>
    public void Start()
    {
        lock (_lock)
        {
            if (_isDisposed || _isShuttingDown)
                return;
            if (_isRunning)
                return;
            _isRunning = true;
            _timer.Start(_interval, OnTimerTick);
        }
        QueueCheck();
    }

    /// <summary>
    /// Stops the periodic timer. Idempotent. Pending coalesced checks are still
    /// allowed to drain, but <see cref="CheckOnce"/> re-checks visibility so a
    /// hidden window never recovers.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (!_isRunning)
                return;
            _isRunning = false;
            _timer.Stop();
        }
    }

    /// <summary>
    /// Coalesces multiple notifications (timer ticks, WM_WINDOWPOSCHANGED) into a
    /// single dispatched check. Multiple concurrent calls only queue one check.
    /// </summary>
    public void QueueCheck()
    {
        if (Volatile.Read(ref _isDisposed) || Volatile.Read(ref _isShuttingDown))
            return;
        if (Interlocked.CompareExchange(ref _checkPending, 1, 0) != 0)
            return;
        try
        {
            _dispatcher.BeginInvoke(OnDispatchedCheck);
        }
        catch
        {
            Interlocked.Exchange(ref _checkPending, 0);
        }
    }

    private void OnTimerTick()
    {
        QueueCheck();
    }

    private void OnDispatchedCheck()
    {
        try
        {
            if (Volatile.Read(ref _isDisposed) || Volatile.Read(ref _isShuttingDown))
                return;
            CheckOnce();
        }
        finally
        {
            Interlocked.Exchange(ref _checkPending, 0);
        }
    }

    /// <summary>
    /// Conditional recovery: only recovers when the overlay is visible, enabled,
    /// not shutting down, and the native WS_EX_TOPMOST bit is missing. When the
    /// bit is present this performs exactly one GetWindowLong and no SetWindowPos.
    /// </summary>
    internal void CheckOnce()
    {
        if (Volatile.Read(ref _isDisposed) || Volatile.Read(ref _isShuttingDown))
            return;
        if (!_target.IsEnabled || !_target.IsVisible)
            return;
        if (_target.IsNativeTopmostSet())
            return;
        _target.RecoverTopmost();
    }

    internal bool IsRunningForTest
    {
        get { lock (_lock) { return _isRunning; } }
    }

    public void PrepareForShutdown()
    {
        lock (_lock)
        {
            if (_isShuttingDown)
                return;
            _isShuttingDown = true;
            _isRunning = false;
            _timer.Stop();
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed)
                return;
            _isDisposed = true;
            _isShuttingDown = true;
            _isRunning = false;
            _timer.Stop();
        }
    }
}
