namespace CodexTPSTray;

internal sealed class MonitorPanelSettingsSynchronizer
{
    private Action<TraySettings>? _panelUpdater;

    public MonitorPanelViewModel ViewModel { get; }

    public MonitorPanelSettingsSynchronizer(TraySettings initialSettings)
    {
        ViewModel = new MonitorPanelViewModel(initialSettings);
    }

    public void AttachPanelUpdater(Action<TraySettings> panelUpdater)
    {
        _panelUpdater = panelUpdater ?? throw new ArgumentNullException(nameof(panelUpdater));
    }

    public void Apply(TraySettings settings)
    {
        if (_panelUpdater != null)
        {
            _panelUpdater(settings);
        }
        else
        {
            ViewModel.UpdateSettings(settings);
        }
    }
}
