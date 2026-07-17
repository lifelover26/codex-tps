using System;
using Microsoft.Win32;

namespace CodexTPSTray;

internal interface ISystemThemeChangeSource
{
    bool Subscribe(Action callback);
    void Unsubscribe();
}

internal sealed class SystemEventsThemeChangeSource : ISystemThemeChangeSource, IDisposable
{
    private readonly Action<UserPreferenceChangedEventHandler> _subscribeAction;
    private readonly Action<UserPreferenceChangedEventHandler> _unsubscribeAction;
    private readonly object _sync = new();
    private UserPreferenceChangedEventHandler? _handler;
    private bool _isSubscribed;
    private bool _isDisposed;

    public SystemEventsThemeChangeSource()
        : this(
            handler => SystemEvents.UserPreferenceChanged += handler,
            handler => SystemEvents.UserPreferenceChanged -= handler
        )
    {
    }

    internal SystemEventsThemeChangeSource(
        Action<UserPreferenceChangedEventHandler> subscribeAction,
        Action<UserPreferenceChangedEventHandler> unsubscribeAction)
    {
        _subscribeAction = subscribeAction ?? throw new ArgumentNullException(nameof(subscribeAction));
        _unsubscribeAction = unsubscribeAction ?? throw new ArgumentNullException(nameof(unsubscribeAction));
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

            try
            {
                _handler = (_, _) => callback();
                _subscribeAction(_handler);
                _isSubscribed = true;
                return true;
            }
            catch
            {
                _handler = null;
                _isSubscribed = false;
                return false;
            }
        }
    }

    public void Unsubscribe()
    {
        lock (_sync)
        {
            if (!_isSubscribed)
                return;

            try
            {
                if (_handler != null)
                {
                    _unsubscribeAction(_handler);
                }
            }
            catch
            {
            }
            finally
            {
                _handler = null;
                _isSubscribed = false;
            }
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
                try
                {
                    if (_handler != null)
                    {
                        _unsubscribeAction(_handler);
                    }
                }
                catch
                {
                }
                finally
                {
                    _handler = null;
                    _isSubscribed = false;
                }
            }
        }
    }
}