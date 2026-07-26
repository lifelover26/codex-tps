using System;

namespace CodexTPSTray;

internal sealed class OverlayLifecycleCoordinator
{
    private readonly IOverlayWindowAdapter _windowAdapter;
    private readonly IDispatcher _dispatcher;
    private bool _isShuttingDown;
    private bool _firstShowQueued;
    private bool _isEnabled;
    private TraySettings _latestSettings = TraySettings.Default;

    public OverlayLifecycleCoordinator(IOverlayWindowAdapter windowAdapter, IDispatcher dispatcher)
    {
        _windowAdapter = windowAdapter;
        _dispatcher = dispatcher;
    }

    public void Initialize(TraySettings settings)
    {
        if (_isShuttingDown)
            return;

        _latestSettings = settings;
        _isEnabled = settings.OverlayEnabled;

        if (_isEnabled && !_firstShowQueued)
        {
            _firstShowQueued = true;
            _dispatcher.BeginInvoke(ExecuteFirstShow);
        }
    }

    public void SetEnabled(TraySettings settings)
    {
        if (_isShuttingDown)
            return;

        _latestSettings = settings;
        _isEnabled = settings.OverlayEnabled;

        if (_isEnabled)
        {
            EnsureVisibleCore();
        }
        else
        {
            _windowAdapter.UpdateSettings(_latestSettings);
            _windowAdapter.Hide();
        }
    }

    public void EnsureVisible(TraySettings settings)
    {
        if (_isShuttingDown)
            return;

        _latestSettings = settings;
        _isEnabled = settings.OverlayEnabled;

        if (!_isEnabled)
            return;

        EnsureVisibleCore();
    }

    public void PrepareForShutdown()
    {
        _isShuttingDown = true;
    }

    private void EnsureVisibleCore()
    {
        if (_isShuttingDown || !_isEnabled)
            return;

        if (_windowAdapter.IsVisible)
            return;

        _windowAdapter.UpdateSettings(_latestSettings);
        _windowAdapter.Show();
        _windowAdapter.ResetPosition(_latestSettings);
    }

    private void ExecuteFirstShow()
    {
        EnsureVisibleCore();
    }
}

internal interface IOverlayWindowAdapter
{
    bool IsVisible { get; }
    void Show();
    void Hide();
    void UpdateSettings(TraySettings settings);
    void ResetPosition(TraySettings settings);
}

internal interface IDispatcher
{
    void BeginInvoke(Action action);
}

internal class WpfDispatcher : IDispatcher
{
    public void BeginInvoke(Action action)
    {
        System.Windows.Application.Current.Dispatcher.BeginInvoke(action, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }
}