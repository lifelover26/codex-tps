using System;
using System.Threading;

namespace CodexTPSTray;

internal sealed class SystemThemeChangeCoordinator : IDisposable
{
    private readonly ISystemThemeChangeSource _source;
    private readonly IDispatcher _dispatcher;
    private readonly Func<TraySettings> _getSettings;
    private readonly Action<TraySettings> _applyTheme;
    private readonly object _lifecycleLock = new();
    private int _pendingRefresh;
    private bool _isShuttingDown;
    private bool _isDisposed;
    private bool _isStarted;

    public SystemThemeChangeCoordinator(
        ISystemThemeChangeSource source,
        IDispatcher dispatcher,
        Func<TraySettings> getSettings,
        Action<TraySettings> applyTheme)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
        _applyTheme = applyTheme ?? throw new ArgumentNullException(nameof(applyTheme));
    }

    public bool Start()
    {
        lock (_lifecycleLock)
        {
            if (_isDisposed || _isShuttingDown)
                return false;

            if (_isStarted)
                return true;

            bool result = _source.Subscribe(OnSystemThemeChanged);
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
    }

    private void OnSystemThemeChanged()
    {
        if (Volatile.Read(ref _isShuttingDown))
            return;

        if (Interlocked.CompareExchange(ref _pendingRefresh, 1, 0) != 0)
            return;

        try
        {
            _dispatcher.BeginInvoke(OnRefreshPending);
        }
        catch
        {
            Interlocked.Exchange(ref _pendingRefresh, 0);
        }
    }

    private void OnRefreshPending()
    {
        try
        {
            if (Volatile.Read(ref _isShuttingDown) || Volatile.Read(ref _isDisposed))
                return;

            var settings = _getSettings();
            if (RequiresRefresh(settings))
            {
                _applyTheme(settings);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _pendingRefresh, 0);
        }
    }

    private static bool RequiresRefresh(TraySettings settings)
    {
        if (settings.ApplicationTheme != ApplicationThemePreference.Light &&
            settings.ApplicationTheme != ApplicationThemePreference.Dark)
        {
            return true;
        }

        if (settings.OverlayTheme == OverlayThemePreference.System)
        {
            return true;
        }

        return false;
    }
}