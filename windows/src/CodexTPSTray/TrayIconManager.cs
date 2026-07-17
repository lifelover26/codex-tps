using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using CodexTPSCore;

namespace CodexTPSTray;

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly SessionScanner _sessionScanner;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _refreshSemaphore;
    private readonly TraySettingsStore _settingsStore;
    private readonly SessionFolderLauncher _sessionFolderLauncher;
    private readonly StartupManager _startupManager;
    private readonly IMonitorWorkAreaProvider _workAreaProvider;

    private MonitorPanelWindow? _monitorPanel;
    private MonitorPanelSettingsSynchronizer? _settingsSynchronizer;
    private OverlayWindow? _overlayWindow;
    private OverlayLifecycleCoordinator? _overlayLifecycle;
    private ThemeApplicationCoordinator? _themeCoordinator;
    private ThemeApplicationTarget? _themeTarget;

    private UsageSnapshot? _latestSnapshot;
    private TraySettings _currentSettings;
    private bool _isShuttingDown;
    private bool _isDisposed;
    private Icon? _trayIcon;

    private readonly ToolStripMenuItem _totalTpsMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _inputTpsMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _cachedTpsMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _outputTpsMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _reasoningTpsMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _requestsMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _activeSessionsMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _cacheRatioMenuItem = new() { Enabled = false };
    private readonly ToolStripMenuItem _statusMenuItem = new() { Enabled = false };

    private readonly Dictionary<MetricWindow, ToolStripMenuItem> _windowMenuItems = new();
    private readonly Dictionary<RefreshCadence, ToolStripMenuItem> _cadenceMenuItems = new();
    private readonly ToolStripMenuItem _openSessionsFolderMenuItem = new();
    private readonly ToolStripMenuItem _launchAtLoginMenuItem = new() { CheckOnClick = true };

    private readonly ToolStripMenuItem _englishLanguageMenuItem = new();
    private readonly ToolStripMenuItem _chineseLanguageMenuItem = new();

    private readonly ToolStripMenuItem _metricsSubmenu = new();
    private readonly ToolStripMenuItem _metricWindowSubmenu = new();
    private readonly ToolStripMenuItem _refreshCadenceSubmenu = new();
    private readonly ToolStripMenuItem _languageSubmenu = new();
    private readonly ToolStripMenuItem _refreshMenuItem = new();
    private readonly ToolStripMenuItem _exitMenuItem = new();

    private readonly ToolStripMenuItem _showOverlayMenuItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem _lockOverlayMenuItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem _resetOverlayPositionMenuItem = new();
    private readonly ToolStripMenuItem _overlaySubmenu = new();
    private readonly ToolStripMenuItem _overlayThemeSubmenu = new();

    private readonly Dictionary<ApplicationThemePreference, ToolStripMenuItem> _applicationThemeMenuItems = new();
    private readonly Dictionary<OverlayThemePreference, ToolStripMenuItem> _overlayThemeMenuItems = new();
    private readonly ToolStripMenuItem _themeSubmenu = new();

    public TrayIconManager() : this(
        TraySettingsStore.CreateDefault(),
        new SessionScanner(),
        new ShellLauncher(),
        new RunKeyStore(),
        () => Environment.ProcessPath,
        new WpfMonitorWorkAreaProvider()
    )
    {
    }

    public TrayIconManager(TraySettingsStore settingsStore, SessionScanner sessionScanner, IShellLauncher shellLauncher, IRunKeyStore runKeyStore, Func<string?> executablePathProvider, IMonitorWorkAreaProvider workAreaProvider)
    {
        _settingsStore = settingsStore;
        _currentSettings = settingsStore.Load();
        _sessionScanner = sessionScanner;
        _sessionFolderLauncher = new SessionFolderLauncher(() => sessionScanner.SessionsRoot, shellLauncher);
        _startupManager = new StartupManager(runKeyStore, executablePathProvider);
        _workAreaProvider = workAreaProvider;

        _notifyIcon = new NotifyIcon();
        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Interval = _currentSettings.RefreshCadence.ToTimeSpan();
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshSemaphore = new SemaphoreSlim(1, 1);

        _settingsSynchronizer = new MonitorPanelSettingsSynchronizer(_currentSettings);

        _themeTarget = new ThemeApplicationTarget(this);
        var systemThemeSource = new WindowsSystemThemeSource();
        var themeResolver = new ThemeResolver(systemThemeSource);
        _themeCoordinator = new ThemeApplicationCoordinator(themeResolver, _themeTarget);
    }

    public void Start()
    {
        InitializeContextMenu();
        InitializeNotifyIcon();
        InitializeLaunchAtLoginState();
        InitializeOverlay();

        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    private void InitializeOverlay()
    {
        _overlayWindow = new OverlayWindow(_workAreaProvider);
        _overlayWindow.DragCompleted += OnOverlayDragCompleted;

        _themeCoordinator?.Apply(_currentSettings);

        var windowAdapter = new OverlayWindowAdapter(_overlayWindow);
        var dispatcher = new WpfDispatcher();
        _overlayLifecycle = new OverlayLifecycleCoordinator(windowAdapter, dispatcher);

        _overlayLifecycle.Initialize(_currentSettings);
    }

    private sealed class OverlayWindowAdapter : IOverlayWindowAdapter
    {
        private readonly OverlayWindow _window;

        public OverlayWindowAdapter(OverlayWindow window) => _window = window;

        public bool IsVisible => _window.IsVisible;

        public void Show() => _window.Show();

        public void Hide() => _window.Hide();

        public void UpdateSettings(TraySettings settings) => _window.UpdateSettings(settings);

        public void ResetPosition(double? left, double? top) => _window.ResetPosition(left, top);
    }

    private sealed class ThemeApplicationTarget : IThemeApplicationTarget
    {
        private readonly TrayIconManager _manager;

        public ThemeApplicationTarget(TrayIconManager manager) => _manager = manager;

        public void ApplyApplicationTheme(EffectiveTheme theme)
        {
            if (_manager._monitorPanel != null)
            {
                _manager._monitorPanel.ApplyTheme(theme);
            }
        }

        public void ApplyOverlayTheme(EffectiveTheme theme)
        {
            if (_manager._overlayWindow != null)
            {
                _manager._overlayWindow.ApplyTheme(theme);
            }
        }
    }

    private void UpdateOverlayContent()
    {
        if (_overlayWindow == null || !_overlayWindow.IsVisible)
            return;

        if (_latestSnapshot.HasValue)
        {
            string[] lines = OverlayFormatter.FormatOverlay(_latestSnapshot.Value, _currentSettings.SelectedWindow, _currentSettings.Language);
            _overlayWindow.UpdateContent(lines);
        }
    }

    private void OnOverlayDragCompleted(double left, double top)
    {
        if (_isShuttingDown)
            return;

        _currentSettings = _currentSettings with { OverlayLeft = left, OverlayTop = top };
        _settingsStore.TrySave(_currentSettings);
    }

    private void InitializeContextMenu()
    {
        var contextMenu = new ContextMenuStrip();

        _metricsSubmenu.Text = Localization.MetricsMenu(_currentSettings.Language);
        _metricsSubmenu.DropDownItems.Add(_totalTpsMenuItem);
        _metricsSubmenu.DropDownItems.Add(_inputTpsMenuItem);
        _metricsSubmenu.DropDownItems.Add(_cachedTpsMenuItem);
        _metricsSubmenu.DropDownItems.Add(_outputTpsMenuItem);
        _metricsSubmenu.DropDownItems.Add(_reasoningTpsMenuItem);
        _metricsSubmenu.DropDownItems.Add(_requestsMenuItem);
        _metricsSubmenu.DropDownItems.Add(_activeSessionsMenuItem);
        _metricsSubmenu.DropDownItems.Add(_cacheRatioMenuItem);
        _metricsSubmenu.DropDownItems.Add(_statusMenuItem);
        contextMenu.Items.Add(_metricsSubmenu);

        contextMenu.Items.Add(new ToolStripSeparator());

        _metricWindowSubmenu.Text = Localization.MetricWindowMenu(_currentSettings.Language);
        foreach (MetricWindow window in Enum.GetValues<MetricWindow>())
        {
            var item = new ToolStripMenuItem(Localization.GetMetricWindowDisplayName(window, _currentSettings.Language));
            item.Click += (sender, e) => OnMetricWindowSelected(window);
            _windowMenuItems[window] = item;
            _metricWindowSubmenu.DropDownItems.Add(item);
        }
        contextMenu.Items.Add(_metricWindowSubmenu);

        _refreshCadenceSubmenu.Text = Localization.RefreshCadenceMenu(_currentSettings.Language);
        foreach (RefreshCadence cadence in Enum.GetValues<RefreshCadence>())
        {
            var item = new ToolStripMenuItem(Localization.GetRefreshCadenceDisplayName(cadence, _currentSettings.Language));
            item.Click += (sender, e) => OnRefreshCadenceSelected(cadence);
            _cadenceMenuItems[cadence] = item;
            _refreshCadenceSubmenu.DropDownItems.Add(item);
        }
        contextMenu.Items.Add(_refreshCadenceSubmenu);

        contextMenu.Items.Add(new ToolStripSeparator());

        _openSessionsFolderMenuItem.Text = Localization.OpenSessionsFolderMenu(_currentSettings.Language);
        _openSessionsFolderMenuItem.Click += OnOpenSessionsFolderClicked;
        contextMenu.Items.Add(_openSessionsFolderMenuItem);

        _launchAtLoginMenuItem.Text = Localization.LaunchAtLogin(_currentSettings.Language);
        _launchAtLoginMenuItem.Click += OnLaunchAtLoginClicked;
        contextMenu.Items.Add(_launchAtLoginMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        _overlaySubmenu.Text = Localization.OverlayMenu(_currentSettings.Language);
        _showOverlayMenuItem.Text = Localization.ShowOverlayMenu(_currentSettings.Language);
        _showOverlayMenuItem.Click += OnShowOverlayClicked;
        _overlaySubmenu.DropDownItems.Add(_showOverlayMenuItem);

        _lockOverlayMenuItem.Text = Localization.LockOverlayMenu(_currentSettings.Language);
        _lockOverlayMenuItem.Click += OnLockOverlayClicked;
        _overlaySubmenu.DropDownItems.Add(_lockOverlayMenuItem);

        _resetOverlayPositionMenuItem.Text = Localization.ResetOverlayPositionMenu(_currentSettings.Language);
        _resetOverlayPositionMenuItem.Click += OnResetOverlayPositionClicked;
        _overlaySubmenu.DropDownItems.Add(_resetOverlayPositionMenuItem);

        _overlaySubmenu.DropDownItems.Add(new ToolStripSeparator());

        _overlayThemeSubmenu.Text = Localization.OverlayThemeMenu(_currentSettings.Language);
        foreach (OverlayThemePreference preference in Enum.GetValues<OverlayThemePreference>())
        {
            var item = new ToolStripMenuItem(Localization.GetOverlayThemeDisplayName(preference, _currentSettings.Language));
            item.Click += (sender, e) => OnOverlayThemeSelected(preference);
            _overlayThemeMenuItems[preference] = item;
            _overlayThemeSubmenu.DropDownItems.Add(item);
        }
        _overlaySubmenu.DropDownItems.Add(_overlayThemeSubmenu);

        contextMenu.Items.Add(_overlaySubmenu);

        contextMenu.Items.Add(new ToolStripSeparator());

        _themeSubmenu.Text = Localization.ThemeMenu(_currentSettings.Language);
        foreach (ApplicationThemePreference preference in Enum.GetValues<ApplicationThemePreference>())
        {
            var item = new ToolStripMenuItem(Localization.GetApplicationThemeDisplayName(preference, _currentSettings.Language));
            item.Click += (sender, e) => OnApplicationThemeSelected(preference);
            _applicationThemeMenuItems[preference] = item;
            _themeSubmenu.DropDownItems.Add(item);
        }
        contextMenu.Items.Add(_themeSubmenu);

        _languageSubmenu.Text = Localization.LanguageMenu(_currentSettings.Language);

        _englishLanguageMenuItem.Text = "English";
        _englishLanguageMenuItem.Click += (sender, e) => OnLanguageSelected(Language.English);
        _languageSubmenu.DropDownItems.Add(_englishLanguageMenuItem);

        _chineseLanguageMenuItem.Text = "简体中文";
        _chineseLanguageMenuItem.Click += (sender, e) => OnLanguageSelected(Language.Chinese);
        _languageSubmenu.DropDownItems.Add(_chineseLanguageMenuItem);

        contextMenu.Items.Add(_languageSubmenu);

        contextMenu.Items.Add(new ToolStripSeparator());

        _refreshMenuItem.Text = Localization.RefreshMenu(_currentSettings.Language);
        _refreshMenuItem.Click += OnRefreshClicked;
        contextMenu.Items.Add(_refreshMenuItem);

        _exitMenuItem.Text = Localization.ExitMenu(_currentSettings.Language);
        _exitMenuItem.Click += OnExitClicked;
        contextMenu.Items.Add(_exitMenuItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        UpdateMenuCheckmarks();
    }

    private void OnShowOverlayClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        bool enabled = _showOverlayMenuItem.Checked;
        _currentSettings = _currentSettings with { OverlayEnabled = enabled };
        _settingsStore.TrySave(_currentSettings);

        _overlayLifecycle?.SetEnabled(_currentSettings);
    }

    private void OnLockOverlayClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        bool locked = _lockOverlayMenuItem.Checked;
        _currentSettings = _currentSettings with { OverlayLocked = locked };
        _settingsStore.TrySave(_currentSettings);

        _overlayWindow?.UpdateSettings(_currentSettings);
    }

    private void OnResetOverlayPositionClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        _currentSettings = _currentSettings with { OverlayLeft = null, OverlayTop = null };
        _settingsStore.TrySave(_currentSettings);

        if (_overlayWindow != null)
        {
            _overlayWindow.ResetPosition();
        }
    }

    private void InitializeNotifyIcon()
    {
        _notifyIcon.Text = "Codex TPS";

        try
        {
            string? processPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(processPath))
            {
                _trayIcon = Icon.ExtractAssociatedIcon(processPath);
            }
        }
        catch
        {
        }

        _notifyIcon.Icon = _trayIcon ?? SystemIcons.Application;
        _notifyIcon.Visible = true;
        _notifyIcon.MouseClick += OnNotifyIconMouseClick;
    }

    private void InitializeLaunchAtLoginState()
    {
        if (_startupManager.TryGetIsEnabled(out bool isEnabled))
        {
            _launchAtLoginMenuItem.Checked = isEnabled;
        }
        else
        {
            ShowBalloonTip(Localization.FailedToReadStartupSettings(_currentSettings.Language), ToolTipIcon.Warning);
        }
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _ = RefreshAsync();
    }

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        _ = RefreshAsync();
    }

    private void OnNotifyIconMouseClick(object? sender, MouseEventArgs e)
    {
        if (_isShuttingDown)
            return;

        if (e.Button != MouseButtons.Left)
            return;

        if (_monitorPanel == null)
        {
            _monitorPanel = new MonitorPanelWindow(_settingsSynchronizer!.ViewModel);
            _monitorPanel.RefreshRequested += () => _ = RefreshAsync();
            _monitorPanel.OpenFolderRequested += () => OnOpenSessionsFolderClicked(null, EventArgs.Empty);
            _monitorPanel.MetricWindowChanged += OnMetricWindowSelected;
            _monitorPanel.RefreshCadenceChanged += OnRefreshCadenceSelected;
            _settingsSynchronizer.AttachPanelUpdater(_monitorPanel.UpdateSettings);
        }

        if (_monitorPanel.IsVisible)
        {
            _monitorPanel.Hide();
        }
        else
        {
            if (_latestSnapshot.HasValue)
            {
                _monitorPanel.UpdateSnapshot(_latestSnapshot.Value);
            }

            _monitorPanel.ApplyTheme(_themeCoordinator?.CurrentApplicationTheme ?? EffectiveTheme.Light);
            _monitorPanel.ShowNearTray();
        }
    }

    private void OnLanguageSelected(Language language)
    {
        if (_isShuttingDown)
            return;

        if (language == _currentSettings.Language)
        {
            UpdateMenuCheckmarks();
            return;
        }

        _currentSettings = _currentSettings with { Language = language };
        _settingsStore.TrySave(_currentSettings);

        UpdateMenuLocalization();
        UpdateMenuCheckmarks();
        ApplySettingsToViewModelAndPanel();
        _overlayWindow?.UpdateSettings(_currentSettings);

        UpdateOverlayContent();

        if (_latestSnapshot.HasValue)
        {
            UpdateUI(_latestSnapshot.Value);
        }
    }

    private void UpdateMenuLocalization()
    {
        var language = _currentSettings.Language;

        foreach (var kvp in _windowMenuItems)
        {
            kvp.Value.Text = Localization.GetMetricWindowDisplayName(kvp.Key, language);
        }

        foreach (var kvp in _cadenceMenuItems)
        {
            kvp.Value.Text = Localization.GetRefreshCadenceDisplayName(kvp.Key, language);
        }

        _metricsSubmenu.Text = Localization.MetricsMenu(language);
        _metricWindowSubmenu.Text = Localization.MetricWindowMenu(language);
        _refreshCadenceSubmenu.Text = Localization.RefreshCadenceMenu(language);
        _languageSubmenu.Text = Localization.LanguageMenu(language);
        _refreshMenuItem.Text = Localization.RefreshMenu(language);
        _exitMenuItem.Text = Localization.ExitMenu(language);

        _openSessionsFolderMenuItem.Text = Localization.OpenSessionsFolderMenu(language);
        _launchAtLoginMenuItem.Text = Localization.LaunchAtLogin(language);

        _overlaySubmenu.Text = Localization.OverlayMenu(language);
        _showOverlayMenuItem.Text = Localization.ShowOverlayMenu(language);
        _lockOverlayMenuItem.Text = Localization.LockOverlayMenu(language);
        _resetOverlayPositionMenuItem.Text = Localization.ResetOverlayPositionMenu(language);

        _themeSubmenu.Text = Localization.ThemeMenu(language);
        foreach (var kvp in _applicationThemeMenuItems)
        {
            kvp.Value.Text = Localization.GetApplicationThemeDisplayName(kvp.Key, language);
        }

        _overlayThemeSubmenu.Text = Localization.OverlayThemeMenu(language);
        foreach (var kvp in _overlayThemeMenuItems)
        {
            kvp.Value.Text = Localization.GetOverlayThemeDisplayName(kvp.Key, language);
        }
    }

    private void OnLaunchAtLoginRequested(bool enabled)
    {
        if (_isShuttingDown)
            return;

        bool success;

        if (enabled)
        {
            success = _startupManager.TryEnable();
        }
        else
        {
            success = _startupManager.TryDisable();
        }

        if (!success)
        {
            _launchAtLoginMenuItem.Checked = !enabled;
            ShowBalloonTip(
                enabled
                    ? Localization.FailedToEnableLaunchAtLogin(_currentSettings.Language)
                    : Localization.FailedToDisableLaunchAtLogin(_currentSettings.Language),
                ToolTipIcon.Warning
            );
        }
        else
        {
            _launchAtLoginMenuItem.Checked = enabled;
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            if (_isShuttingDown)
                return;

            if (!await _refreshSemaphore.WaitAsync(0))
                return;

            try
            {
                if (_isShuttingDown)
                    return;

                _monitorPanel?.SetIsRefreshing(true);

                try
                {
                    var snapshot = await Task.Run(() => _sessionScanner.Refresh(DateTimeOffset.UtcNow));

                    if (_isShuttingDown)
                        return;

                    _latestSnapshot = snapshot;
                    UpdateUI(snapshot);
                    _overlayLifecycle?.EnsureVisible(_currentSettings);
                    UpdateOverlayContent();

                    _monitorPanel?.UpdateSnapshot(snapshot);
                }
                finally
                {
                    if (!_isShuttingDown)
                    {
                        _monitorPanel?.SetIsRefreshing(false);
                    }
                }
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }
        catch
        {
            if (!_isShuttingDown)
            {
                _monitorPanel?.SetIsRefreshing(false);
            }
        }
    }

    private void UpdateUI(UsageSnapshot snapshot)
    {
        if (_isShuttingDown)
            return;

        var metrics = _currentSettings.SelectedWindow.GetMetrics(snapshot);
        var language = _currentSettings.Language;

        _totalTpsMenuItem.Text = $"{Localization.Total(language)}: {metrics.TokensPerSecond:F1} token/s";
        _inputTpsMenuItem.Text = $"{Localization.Input(language)}: {metrics.InputTokensPerSecond:F1} token/s";
        _cachedTpsMenuItem.Text = $"{Localization.Cached(language)}: {metrics.CachedInputTokensPerSecond:F1} token/s";
        _outputTpsMenuItem.Text = $"{Localization.Output(language)}: {metrics.OutputTokensPerSecond:F1} token/s";
        _reasoningTpsMenuItem.Text = $"{Localization.Reasoning(language)}: {metrics.ReasoningTokensPerSecond:F1} token/s";
        _requestsMenuItem.Text = $"{Localization.RequestsPerMinute(language)}: {metrics.RequestsPerMinute:F1}/min";
        _activeSessionsMenuItem.Text = $"{Localization.ActiveSessions(language)}: {snapshot.ActiveSessions}";
        _cacheRatioMenuItem.Text = $"{Localization.CacheRatio(language)}: {metrics.CacheRatio:P0}";
        _statusMenuItem.Text = $"{Localization.Status(language)}: {Localization.GetStatusText(snapshot.Status, false, snapshot.MalformedRelevantLines, true, language)}";

        string tooltip = TrayTextFormatter.FormatTooltip(snapshot, _currentSettings.SelectedWindow, language);
        _notifyIcon.Text = tooltip;
    }

    private void OnMetricWindowSelected(MetricWindow window)
    {
        if (_isShuttingDown || window == _currentSettings.SelectedWindow)
            return;

        _currentSettings = _currentSettings with { SelectedWindow = window };
        _settingsStore.TrySave(_currentSettings);

        UpdateMenuCheckmarks();
        ApplySettingsToViewModelAndPanel();
        _overlayWindow?.UpdateSettings(_currentSettings);

        UpdateOverlayContent();

        if (_latestSnapshot.HasValue)
        {
            UpdateUI(_latestSnapshot.Value);
        }
    }

    private void OnRefreshCadenceSelected(RefreshCadence cadence)
    {
        if (_isShuttingDown || cadence == _currentSettings.RefreshCadence)
            return;

        _currentSettings = _currentSettings with { RefreshCadence = cadence };
        _settingsStore.TrySave(_currentSettings);

        UpdateMenuCheckmarks();
        ApplySettingsToViewModelAndPanel();

        _refreshTimer.Stop();
        _refreshTimer.Interval = cadence.ToTimeSpan();
        _refreshTimer.Start();
    }

    private void OnApplicationThemeSelected(ApplicationThemePreference preference)
    {
        if (_isShuttingDown)
            return;

        _currentSettings = _currentSettings with { ApplicationTheme = preference };
        _settingsStore.TrySave(_currentSettings);

        _themeCoordinator?.Apply(_currentSettings);
        UpdateMenuCheckmarks();
    }

    private void OnOverlayThemeSelected(OverlayThemePreference preference)
    {
        if (_isShuttingDown)
            return;

        _currentSettings = _currentSettings with { OverlayTheme = preference };
        _settingsStore.TrySave(_currentSettings);

        _themeCoordinator?.Apply(_currentSettings);
        UpdateMenuCheckmarks();
    }

    private void ApplySettingsToViewModelAndPanel()
    {
        _settingsSynchronizer?.Apply(_currentSettings);
    }

    private void OnOpenSessionsFolderClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        if (!_sessionFolderLauncher.OpenSessionsFolder())
        {
            ShowBalloonTip(Localization.FailedToOpenSessionsFolder(_currentSettings.Language), ToolTipIcon.Warning);
        }
    }

    private void OnLaunchAtLoginClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        bool requestedEnabled = _launchAtLoginMenuItem.Checked;
        bool success;

        if (requestedEnabled)
        {
            success = _startupManager.TryEnable();
        }
        else
        {
            success = _startupManager.TryDisable();
        }

        if (!success)
        {
            _launchAtLoginMenuItem.Checked = !requestedEnabled;
            ShowBalloonTip(
                requestedEnabled
                    ? Localization.FailedToEnableLaunchAtLogin(_currentSettings.Language)
                    : Localization.FailedToDisableLaunchAtLogin(_currentSettings.Language),
                ToolTipIcon.Warning
            );
        }
    }

    private void ShowBalloonTip(string message, ToolTipIcon icon)
    {
        try
        {
            _notifyIcon.ShowBalloonTip(3000, "Codex TPS", message, icon);
        }
        catch
        {
        }
    }

    private void UpdateMenuCheckmarks()
    {
        foreach (var kvp in _windowMenuItems)
        {
            kvp.Value.Checked = kvp.Key == _currentSettings.SelectedWindow;
        }

        foreach (var kvp in _cadenceMenuItems)
        {
            kvp.Value.Checked = kvp.Key == _currentSettings.RefreshCadence;
        }

        _englishLanguageMenuItem.Checked = _currentSettings.Language == Language.English;
        _chineseLanguageMenuItem.Checked = _currentSettings.Language == Language.Chinese;

        _showOverlayMenuItem.Checked = _currentSettings.OverlayEnabled;
        _lockOverlayMenuItem.Checked = _currentSettings.OverlayLocked;

        var applicationTheme = _currentSettings.ApplicationTheme switch
        {
            ApplicationThemePreference.System => ApplicationThemePreference.System,
            ApplicationThemePreference.Light => ApplicationThemePreference.Light,
            ApplicationThemePreference.Dark => ApplicationThemePreference.Dark,
            _ => ApplicationThemePreference.System
        };

        foreach (var kvp in _applicationThemeMenuItems)
        {
            kvp.Value.Checked = kvp.Key == applicationTheme;
        }

        var overlayTheme = _currentSettings.OverlayTheme switch
        {
            OverlayThemePreference.FollowApplication => OverlayThemePreference.FollowApplication,
            OverlayThemePreference.System => OverlayThemePreference.System,
            OverlayThemePreference.Light => OverlayThemePreference.Light,
            OverlayThemePreference.Dark => OverlayThemePreference.Dark,
            _ => OverlayThemePreference.FollowApplication
        };

        foreach (var kvp in _overlayThemeMenuItems)
        {
            kvp.Value.Checked = kvp.Key == overlayTheme;
        }
    }

    private void OnExitClicked(object? sender, EventArgs e)
    {
        Shutdown();
    }

    private void Shutdown()
    {
        if (_isShuttingDown)
            return;

        _isShuttingDown = true;

        _refreshTimer.Stop();

        _overlayLifecycle?.PrepareForShutdown();
        _overlayWindow?.PrepareForShutdown();
        _monitorPanel?.PrepareForShutdown();

        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isDisposed)
            return;

        _isDisposed = true;
        _isShuttingDown = true;

        if (disposing)
        {
            _refreshTimer.Stop();

            _overlayLifecycle?.PrepareForShutdown();
            _overlayLifecycle = null;

            _overlayWindow?.PrepareForShutdown();
            _overlayWindow = null;

            _monitorPanel?.PrepareForShutdown();
            _monitorPanel = null;

            _notifyIcon.Visible = false;
            _notifyIcon.Icon = null;
            _notifyIcon.Dispose();
            _trayIcon?.Dispose();
        }
    }
}