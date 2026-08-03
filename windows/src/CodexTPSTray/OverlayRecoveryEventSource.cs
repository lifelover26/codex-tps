using System;
using Microsoft.Win32;

namespace CodexTPSTray;

internal interface IOverlayRecoveryEventSource
{
    bool Subscribe(Action callback);
    void Unsubscribe();
}

internal sealed class SystemEventsOverlayRecoverySource : IOverlayRecoveryEventSource, IDisposable
{
    private readonly Action<PowerModeChangedEventHandler> _subscribePowerMode;
    private readonly Action<PowerModeChangedEventHandler> _unsubscribePowerMode;
    private readonly Action<SessionSwitchEventHandler> _subscribeSessionSwitch;
    private readonly Action<SessionSwitchEventHandler> _unsubscribeSessionSwitch;
    private readonly Action<EventHandler> _subscribeDisplaySettings;
    private readonly Action<EventHandler> _unsubscribeDisplaySettings;
    private readonly object _sync = new();
    private PowerModeChangedEventHandler? _powerHandler;
    private SessionSwitchEventHandler? _sessionHandler;
    private EventHandler? _displayHandler;
    private Action? _callback;
    private bool _isSubscribed;
    private bool _isDisposed;
    private bool _powerSubscribed;
    private bool _sessionSubscribed;
    private bool _displaySubscribed;

    public SystemEventsOverlayRecoverySource()
        : this(
            handler => SystemEvents.PowerModeChanged += handler,
            handler => SystemEvents.PowerModeChanged -= handler,
            handler => SystemEvents.SessionSwitch += handler,
            handler => SystemEvents.SessionSwitch -= handler,
            handler => SystemEvents.DisplaySettingsChanged += handler,
            handler => SystemEvents.DisplaySettingsChanged -= handler
        )
    {
    }

    internal SystemEventsOverlayRecoverySource(
        Action<PowerModeChangedEventHandler> subscribePowerMode,
        Action<PowerModeChangedEventHandler> unsubscribePowerMode,
        Action<SessionSwitchEventHandler> subscribeSessionSwitch,
        Action<SessionSwitchEventHandler> unsubscribeSessionSwitch,
        Action<EventHandler> subscribeDisplaySettings,
        Action<EventHandler> unsubscribeDisplaySettings)
    {
        _subscribePowerMode = subscribePowerMode ?? throw new ArgumentNullException(nameof(subscribePowerMode));
        _unsubscribePowerMode = unsubscribePowerMode ?? throw new ArgumentNullException(nameof(unsubscribePowerMode));
        _subscribeSessionSwitch = subscribeSessionSwitch ?? throw new ArgumentNullException(nameof(subscribeSessionSwitch));
        _unsubscribeSessionSwitch = unsubscribeSessionSwitch ?? throw new ArgumentNullException(nameof(unsubscribeSessionSwitch));
        _subscribeDisplaySettings = subscribeDisplaySettings ?? throw new ArgumentNullException(nameof(subscribeDisplaySettings));
        _unsubscribeDisplaySettings = unsubscribeDisplaySettings ?? throw new ArgumentNullException(nameof(unsubscribeDisplaySettings));
    }

    public bool Subscribe(Action callback)
    {
        if (callback == null)
            return false;

        lock (_sync)
        {
            if (_isDisposed)
                return false;

            if (_isSubscribed)
                return true;

            _callback = callback;
            _powerHandler = OnPowerModeChanged;
            _sessionHandler = OnSessionSwitch;
            _displayHandler = OnDisplaySettingsChanged;

            try
            {
                _subscribePowerMode(_powerHandler);
                _powerSubscribed = true;
            }
            catch
            {
                CleanupHandlers();
                return false;
            }

            try
            {
                _subscribeSessionSwitch(_sessionHandler);
                _sessionSubscribed = true;
            }
            catch
            {
                BestEffortDetach(power: true, session: false, display: false);
                CleanupHandlers();
                return false;
            }

            try
            {
                _subscribeDisplaySettings(_displayHandler);
                _displaySubscribed = true;
            }
            catch
            {
                BestEffortDetach(power: true, session: true, display: false);
                CleanupHandlers();
                return false;
            }

            _isSubscribed = true;
            return true;
        }
    }

    public void Unsubscribe()
    {
        lock (_sync)
        {
            if (!_isSubscribed)
                return;

            BestEffortDetach(power: _powerSubscribed, session: _sessionSubscribed, display: _displaySubscribed);
            CleanupHandlers();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_isDisposed)
                return;

            _isDisposed = true;

            if (_isSubscribed)
            {
                BestEffortDetach(power: _powerSubscribed, session: _sessionSubscribed, display: _displaySubscribed);
                CleanupHandlers();
            }
        }
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume)
        {
            _callback?.Invoke();
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
    {
        switch (e.Reason)
        {
            case SessionSwitchReason.SessionUnlock:
            case SessionSwitchReason.ConsoleConnect:
            case SessionSwitchReason.RemoteConnect:
                _callback?.Invoke();
                break;
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        _callback?.Invoke();
    }

    private void BestEffortDetach(bool power, bool session, bool display)
    {
        // Unsubscribe in reverse subscription order (display → session → power) so that
        // if any individual unsubscribe throws, the others are still attempted. Each
        // call is wrapped in its own try/catch to isolate failures.
        if (display && _displayHandler != null)
        {
            try { _unsubscribeDisplaySettings(_displayHandler); } catch { }
        }
        if (session && _sessionHandler != null)
        {
            try { _unsubscribeSessionSwitch(_sessionHandler); } catch { }
        }
        if (power && _powerHandler != null)
        {
            try { _unsubscribePowerMode(_powerHandler); } catch { }
        }
    }

    private void CleanupHandlers()
    {
        _powerHandler = null;
        _sessionHandler = null;
        _displayHandler = null;
        _callback = null;
        _powerSubscribed = false;
        _sessionSubscribed = false;
        _displaySubscribed = false;
        _isSubscribed = false;
    }
}
