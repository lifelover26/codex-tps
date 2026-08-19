using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Threading;
using CodexTPSCore;

namespace CodexTPSTray;

internal interface IOverlayMenuCommandHandler
{
    void ToggleOverlay(bool enabled);
    void ToggleLock(bool locked);
    void SelectPosition(OverlayPositionPreset preset);
    void SelectOpacity(OverlayOpacityPreference opacity);
    void SelectTheme(OverlayThemePreference theme);
    void SelectPositionMemoryMode(OverlayPositionMemoryMode mode);
    void SelectAppearanceMemoryMode(OverlayAppearanceMemoryMode mode);
}

internal interface IOverlayMenuStateProvider
{
    TraySettings CurrentSettings { get; }
    bool IsOverlayVisible { get; }
}

public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private SessionScanner _sessionScanner;
    private readonly DispatcherTimer _refreshTimer;
    private readonly SemaphoreSlim _refreshSemaphore;
    private readonly ITraySettingsStore _settingsStore;
    private readonly CancellationToken _shutdownToken;
    private readonly SessionFolderLauncher _sessionFolderLauncher;
    private readonly StartupManager _startupManager;
    private readonly IMonitorWorkAreaProvider _workAreaProvider;
    private readonly ICodexDataSourceResolver _dataSourceResolver;
    private readonly IWslCodexHomeDiscovery _wslDiscovery;
    private readonly CancellationTokenSource _shutdownCts;
    private readonly IUiDispatcher _uiDispatcher;

    private MonitorPanelWindow? _monitorPanel;
    private MonitorPanelSettingsSynchronizer? _settingsSynchronizer;
    private OverlayWindow? _overlayWindow;
    private OverlayLifecycleCoordinator? _overlayLifecycle;
    private ThemeApplicationCoordinator? _themeCoordinator;
    private ThemeApplicationTarget? _themeTarget;
    private ThemeResolver? _themeResolver;
    private readonly IWinFormsThemeApplier _winFormsThemeApplier;
    private ContextMenuStrip? _contextMenu;
    private TrayMenuRenderer? _trayMenuRenderer;
    private SystemThemeChangeCoordinator? _systemThemeChangeCoordinator;
    private OverlayTopmostRecoveryCoordinator? _overlayRecoveryCoordinator;

    private UsageSnapshot? _latestSnapshot;
    private TraySettings _currentSettings;
    private bool _isShuttingDown;
    private bool _isDisposed;
    private Icon? _trayIcon;

    private readonly Dictionary<MetricWindow, ToolStripMenuItem> _windowMenuItems = new();
    private readonly Dictionary<RefreshCadence, ToolStripMenuItem> _cadenceMenuItems = new();
    private readonly ToolStripMenuItem _openSessionsFolderMenuItem = new();
    private readonly ToolStripMenuItem _launchAtLoginMenuItem = new() { CheckOnClick = true };

    private readonly ToolStripMenuItem _englishLanguageMenuItem = new();
    private readonly ToolStripMenuItem _chineseLanguageMenuItem = new();

    private readonly ToolStripMenuItem _metricWindowSubmenu = new();
    private readonly ToolStripMenuItem _refreshCadenceSubmenu = new();
    private readonly ToolStripMenuItem _languageSubmenu = new();
    private readonly ToolStripMenuItem _refreshMenuItem = new();
    private readonly ToolStripMenuItem _exitMenuItem = new();

    private readonly ToolStripMenuItem _showOverlayMenuItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem _lockOverlayMenuItem = new() { CheckOnClick = true };
    private readonly ToolStripMenuItem _overlaySubmenu = new();
    private readonly ToolStripMenuItem _overlayThemeSubmenu = new();
    private readonly ToolStripMenuItem _overlayPositionSubmenu = new();
    private readonly ToolStripMenuItem _memorySubmenu = new();
    private readonly ToolStripMenuItem _appearanceMemorySubmenu = new();
    private readonly ToolStripMenuItem _positionMemorySubmenu = new();
    private readonly Dictionary<OverlayAppearanceMemoryMode, ToolStripMenuItem> _appearanceMemoryMenuItems = new();
    private readonly Dictionary<OverlayPositionMemoryMode, ToolStripMenuItem> _positionMemoryMenuItems = new();
    private readonly ToolStripMenuItem _overlayOpacitySubmenu = new();

    private readonly Dictionary<ApplicationThemePreference, ToolStripMenuItem> _applicationThemeMenuItems = new();
    private readonly Dictionary<OverlayThemePreference, ToolStripMenuItem> _overlayThemeMenuItems = new();
    private readonly Dictionary<OverlayPositionPreset, ToolStripMenuItem> _overlayPositionMenuItems = new();
    private readonly Dictionary<OverlayOpacityPreference, ToolStripMenuItem> _overlayOpacityMenuItems = new();
    private readonly ToolStripMenuItem _themeSubmenu = new();
    private readonly ToolStripMenuItem _dataSourceSubmenu = new();
    private readonly ToolStripMenuItem _windowsDataSourceMenuItem = new();
    private readonly Dictionary<string, ToolStripMenuItem> _wslDataSourceMenuItems = new();
    private int _isDiscoveringWsl;
    private readonly SemaphoreSlim _switchSemaphore = new(1);
    private bool _isScannerReady;
    private bool _isSwitchingDataSource;
    private IReadOnlyList<ResolvedCodexDataSource>? _discoveredWslSources;

    internal static TrayIconManager CreateDefault()
    {
        var settingsStore = TraySettingsStore.CreateDefault();
        var settings = settingsStore.Load();

        var systemThemeSource = new WindowsSystemThemeSource();
        var themeResolver = new ThemeResolver(systemThemeSource);
        var resolvedTheme = themeResolver.ResolveApplication(settings.ApplicationTheme);

        var winFormsThemeApplier = new WindowsFormsThemeApplier();
        winFormsThemeApplier.TryApply(resolvedTheme);

        return new TrayIconManager(
            settingsStore,
            settings,
            new SessionScanner(),
            new ShellLauncher(),
            new RunKeyStore(),
            () => Environment.ProcessPath,
            new WpfMonitorWorkAreaProvider(new WindowsDisplayIdentityProvider()),
            winFormsThemeApplier,
            themeResolver
        );
    }

    public TrayIconManager(TraySettingsStore settingsStore, SessionScanner sessionScanner, IShellLauncher shellLauncher, IRunKeyStore runKeyStore, Func<string?> executablePathProvider, IMonitorWorkAreaProvider workAreaProvider)
        : this(
            settingsStore,
            settingsStore.Load(),
            sessionScanner,
            shellLauncher,
            runKeyStore,
            executablePathProvider,
            workAreaProvider,
            new WindowsFormsThemeApplier(),
            new ThemeResolver(new WindowsSystemThemeSource())
        )
    {
    }

    internal TrayIconManager(ITraySettingsStore settingsStore, TraySettings initialSettings, SessionScanner sessionScanner, IShellLauncher shellLauncher, IRunKeyStore runKeyStore, Func<string?> executablePathProvider, IMonitorWorkAreaProvider workAreaProvider, IWinFormsThemeApplier winFormsThemeApplier, ThemeResolver themeResolver, ICodexDataSourceResolver? dataSourceResolver = null, IWslCodexHomeDiscovery? wslDiscovery = null, IUiDispatcher? uiDispatcher = null)
    {
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _currentSettings = initialSettings ?? throw new ArgumentNullException(nameof(initialSettings));
        _sessionScanner = sessionScanner ?? throw new ArgumentNullException(nameof(sessionScanner));
        _workAreaProvider = workAreaProvider ?? throw new ArgumentNullException(nameof(workAreaProvider));
        _winFormsThemeApplier = winFormsThemeApplier ?? throw new ArgumentNullException(nameof(winFormsThemeApplier));
        if (themeResolver == null) throw new ArgumentNullException(nameof(themeResolver));

        var wslRunner = new WslProcessRunner();
        var directoryChecker = new CodexHomeDirectoryChecker();
        _wslDiscovery = wslDiscovery ?? new WslCodexHomeDiscovery(wslRunner, directoryChecker);
        _dataSourceResolver = dataSourceResolver ?? new CodexDataSourceResolver(_wslDiscovery);
        _shutdownCts = new CancellationTokenSource();
        _shutdownToken = _shutdownCts.Token;
        _uiDispatcher = uiDispatcher ?? new WpfUiDispatcher();

        _sessionFolderLauncher = new SessionFolderLauncher(() => _sessionScanner.SessionsRoot, shellLauncher);
        _startupManager = new StartupManager(runKeyStore, executablePathProvider);

        _notifyIcon = new NotifyIcon();
        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Interval = _currentSettings.RefreshCadence.ToTimeSpan();
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshSemaphore = new SemaphoreSlim(1, 1);

        _settingsSynchronizer = new MonitorPanelSettingsSynchronizer(_currentSettings);

        _themeTarget = new ThemeApplicationTarget(this);
        _themeResolver = themeResolver;
        _themeCoordinator = new ThemeApplicationCoordinator(themeResolver, _themeTarget);

        _isScannerReady = _currentSettings.DataSource.Kind == CodexDataSourceKind.Windows;
    }

    public void Start()
    {
        InitializeContextMenu();
        InitializeNotifyIcon();
        InitializeLaunchAtLoginState();
        InitializeOverlay();

        InitializeSystemThemeListener();
        InitializeOverlayRecoveryListener();

        StartupTask = StartAsync();
    }

    internal Task StartupTask { get; private set; } = Task.CompletedTask;

    internal bool IsScannerReady => _isScannerReady;
    internal TraySettings CurrentSettings => _currentSettings;
    internal SessionScanner SessionScanner => _sessionScanner;
    internal UsageSnapshot? LatestSnapshot => _latestSnapshot;
    internal IReadOnlyDictionary<string, ToolStripMenuItem> WslDataSourceMenuItems => _wslDataSourceMenuItems;
    internal bool IsRefreshTimerEnabled => _refreshTimer.IsEnabled;
    internal bool IsShuttingDown => _isShuttingDown;
    internal SemaphoreSlim RefreshSemaphore => _refreshSemaphore;
    internal SemaphoreSlim SwitchSemaphore => _switchSemaphore;
    internal bool WindowsDataSourceMenuItemChecked => _windowsDataSourceMenuItem.Checked;
    internal ToolStripMenuItem WindowsDataSourceMenuItem => _windowsDataSourceMenuItem;
    internal ToolStripMenuItem LockOverlayMenuItemForTest => _lockOverlayMenuItem;
    internal ToolStripMenuItem ShowOverlayMenuItemForTest => _showOverlayMenuItem;
    internal ContextMenuStrip? ContextMenuStrip => _contextMenu;
    internal TrayMenuRenderer? TrayMenuRendererForTest => _trayMenuRenderer;
    internal Task? DataSourceDiscoveryTask { get; private set; }
    internal Task? LastDataSourceSwitchTask { get; private set; }
    internal Action? OnSwitchRejected { get; set; }
    internal Action? OnSwitchAcquiringRefreshSemaphore { get; set; }
    internal bool IsSwitchingDataSource => _isSwitchingDataSource;

    internal void RaiseRefreshTimerTick() => OnRefreshTimerTick(null, EventArgs.Empty);

    internal void SetSessionScanner(SessionScanner scanner) => _sessionScanner = scanner;

    internal void RaiseLanguageSelected(Language language) => OnLanguageSelected(language);

    private async Task StartAsync()
    {
        if (_currentSettings.DataSource.Kind == CodexDataSourceKind.Wsl)
        {
            await StartWslAsync(_currentSettings.DataSource, _shutdownToken).ConfigureAwait(false);
            return;
        }

        _uiDispatcher.Invoke(() =>
        {
            if (!_isShuttingDown)
            {
                _refreshTimer.Start();
                _ = RefreshAsync();
            }
        });
    }

    private async Task StartWslAsync(CodexDataSourceSelection selection, CancellationToken cancellationToken)
    {
        var resolved = await _dataSourceResolver.ResolveAsync(selection, cancellationToken).ConfigureAwait(false);

        _uiDispatcher.Invoke(() =>
        {
            if (_isShuttingDown)
                return;

            // If the user switched data sources (or the app began shutting down)
            // while resolve was in flight, do nothing: a stale success must not
            // overwrite the new selection, and a stale failure must not show a
            // misleading balloon for a selection the user already abandoned.
            if (!_currentSettings.DataSource.Equals(selection))
                return;

            if (resolved == null)
            {
                ShowBalloonTip(Localization.DataSourceStartupWslFailed(_currentSettings.Language), ToolTipIcon.Warning);
                return;
            }

            var newScanner = new SessionScanner(resolved.CodexHome);
            _sessionScanner = newScanner;
            _isScannerReady = true;
            if (!_refreshTimer.IsEnabled)
            {
                _refreshTimer.Start();
            }
            _ = RefreshAsync();
        });
    }

    private void InitializeOverlay()
    {
        _overlayWindow = new OverlayWindow(_workAreaProvider);
        _overlayWindow.DragCompleted += OnOverlayDragCompleted;
        _overlayWindow.CustomPositionSettingsUpdated += OnOverlayCustomPositionSettingsUpdated;
        _overlayWindow.SetOverlayMenuCommandHandler(new OverlayMenuCommandHandler(this));
        _overlayWindow.SetOverlayMenuStateProvider(new OverlayMenuStateProvider(this));

        _themeCoordinator?.Apply(_currentSettings);
        ReconcileOverlayAppearance();

        var windowAdapter = new OverlayWindowAdapter(_overlayWindow);
        var dispatcher = new WpfDispatcher();
        _overlayLifecycle = new OverlayLifecycleCoordinator(windowAdapter, dispatcher);

        _overlayLifecycle.Initialize(_currentSettings);
        UpdateOverlayContextMenu();
    }

    private sealed class OverlayMenuCommandHandler : IOverlayMenuCommandHandler
    {
        private readonly TrayIconManager _manager;

        public OverlayMenuCommandHandler(TrayIconManager manager) => _manager = manager;

        public void ToggleOverlay(bool enabled) => _manager.ToggleOverlayEnabled(enabled);
        public void ToggleLock(bool locked) => _manager.ToggleOverlayLocked(locked);
        public void SelectPosition(OverlayPositionPreset preset) => _manager.SelectOverlayPosition(preset);
        public void SelectOpacity(OverlayOpacityPreference opacity) => _manager.SelectOverlayOpacity(opacity);
        public void SelectTheme(OverlayThemePreference theme) => _manager.SelectOverlayTheme(theme);
        public void SelectPositionMemoryMode(OverlayPositionMemoryMode mode) => _manager.SelectOverlayPositionMemoryMode(mode);
        public void SelectAppearanceMemoryMode(OverlayAppearanceMemoryMode mode) => _manager.SelectOverlayAppearanceMemoryMode(mode);
    }

    private sealed class OverlayMenuStateProvider : IOverlayMenuStateProvider
    {
        private readonly TrayIconManager _manager;

        public OverlayMenuStateProvider(TrayIconManager manager) => _manager = manager;

        public TraySettings CurrentSettings => _manager._currentSettings;
        public bool IsOverlayVisible => _manager._overlayWindow?.IsVisible ?? false;
    }

    private void ToggleOverlayEnabled(bool enabled)
    {
        if (_isShuttingDown)
            return;

        _currentSettings = _currentSettings with { OverlayEnabled = enabled };
        _settingsStore.TrySave(_currentSettings);

        _overlayLifecycle?.SetEnabled(_currentSettings);
        ReconcileOverlayAppearance();
        UpdateMenuCheckmarks();
        UpdateOverlayContextMenu();
    }

    private void ToggleOverlayLocked(bool locked)
    {
        if (_isShuttingDown)
            return;

        _currentSettings = _currentSettings with { OverlayLocked = locked };
        _settingsStore.TrySave(_currentSettings);

        _overlayWindow?.UpdateSettings(_currentSettings);
        UpdateMenuCheckmarks();
        UpdateOverlayContextMenu();
    }

    private void SelectOverlayPosition(OverlayPositionPreset preset)
    {
        OnOverlayPositionPresetSelected(preset);
        UpdateOverlayContextMenu();
    }

    private void SelectOverlayOpacity(OverlayOpacityPreference preference)
    {
        OnOverlayOpacitySelected(preference);
        UpdateOverlayContextMenu();
    }

    private void SelectOverlayTheme(OverlayThemePreference preference)
    {
        OnOverlayThemeSelected(preference);
        UpdateOverlayContextMenu();
    }

    private void SelectOverlayPositionMemoryMode(OverlayPositionMemoryMode mode)
    {
        if (_isShuttingDown || _overlayWindow == null)
            return;

        var rect = _overlayWindow.GetCurrentRect();
        var (allMonitors, primaryMonitor) = GetMonitorsAndPrimary();

        if (rect != null)
        {
            var (rectLeft, rectTop, width, height) = rect.Value;
            _currentSettings = OverlayPositionCoordinator.SwitchMemoryMode(
                _currentSettings,
                mode,
                rectLeft,
                rectTop,
                width,
                height,
                allMonitors,
                primaryMonitor);
        }
        else
        {
            _currentSettings = OverlayPositionCoordinator.ApplyMemoryModeSwitch(_currentSettings, mode);
        }

        _settingsStore.TrySave(_currentSettings);
        _overlayWindow.UpdateSettings(_currentSettings);
        UpdateMenuCheckmarks();
        UpdateOverlayContextMenu();
    }

    private void SelectOverlayAppearanceMemoryMode(OverlayAppearanceMemoryMode mode)
    {
        if (_isShuttingDown || _overlayWindow == null)
            return;

        var targetMonitor = ResolveTargetMonitorForMenu();
        _currentSettings = OverlayAppearanceCoordinator.SwitchMemoryMode(
            _currentSettings, mode, targetMonitor);
        _settingsStore.TrySave(_currentSettings);

        _overlayWindow.UpdateSettings(_currentSettings);
        ApplyOverlayAppearance();
        UpdateMenuCheckmarks();
        UpdateOverlayContextMenu();
    }

    private void UpdateOverlayContextMenu()
    {
        _overlayWindow?.UpdateContextMenuState();
    }

    private void InitializeSystemThemeListener()
    {
        var source = new SystemEventsThemeChangeSource();
        var dispatcher = new WpfDispatcher();
        _systemThemeChangeCoordinator = new SystemThemeChangeCoordinator(
            source,
            dispatcher,
            () => _currentSettings,
            settings => _themeCoordinator?.Apply(settings)
        );
        _systemThemeChangeCoordinator.Start();
    }

    private void InitializeOverlayRecoveryListener()
    {
        try
        {
            var source = new SystemEventsOverlayRecoverySource();
            var dispatcher = new WpfDispatcher();
            var target = new OverlayRecoveryTarget(this);
            _overlayRecoveryCoordinator = new OverlayTopmostRecoveryCoordinator(source, dispatcher, target);
            _overlayRecoveryCoordinator.Start();
        }
        catch
        {
        }
    }

    private sealed class OverlayWindowAdapter : IOverlayWindowAdapter
    {
        private readonly OverlayWindow _window;

        public OverlayWindowAdapter(OverlayWindow window) => _window = window;

        public bool IsVisible => _window.IsVisible;

        public void Show() => _window.Show();

        public void Hide() => _window.Hide();

        public void UpdateSettings(TraySettings settings) => _window.UpdateSettings(settings);

        public void ResetPosition(TraySettings settings) => _window.ResetPosition(settings);
    }

    private sealed class OverlayRecoveryTarget : IOverlayTopmostRecoveryTarget
    {
        private readonly TrayIconManager _manager;

        public OverlayRecoveryTarget(TrayIconManager manager) => _manager = manager;

        public bool IsVisible => _manager._overlayWindow?.IsVisible ?? false;

        public bool IsEnabled => _manager._currentSettings.OverlayEnabled && !_manager._isShuttingDown;

        public void ReassertTopmost()
        {
            if (_manager._isShuttingDown)
                return;
            _manager._overlayWindow?.ReassertTopmost();
        }

        public void ReconcilePlacement()
        {
            if (_manager._isShuttingDown)
                return;

            if (_manager._overlayWindow == null)
                return;

            _manager._overlayWindow.ReconcilePlacement();
            _manager.ReconcileOverlayAppearance();
            _manager.UpdateMenuCheckmarks();
            _manager.UpdateOverlayContextMenu();
        }
    }

    private sealed class ThemeApplicationTarget : IThemeApplicationTarget
    {
        private readonly TrayIconManager _manager;

        public ThemeApplicationTarget(TrayIconManager manager) => _manager = manager;

        public void ApplyApplicationTheme(EffectiveTheme theme)
        {
            _manager._winFormsThemeApplier.TryApply(theme);

            if (_manager._trayMenuRenderer != null)
            {
                _manager._trayMenuRenderer.TryUpdateTheme(theme);

                // Re-apply layout configuration to root and all nested menus so
                // newly created DropDowns (e.g. WSL items added after Start) get
                // the renderer and compact settings.
                if (_manager._contextMenu != null)
                {
                    _manager._trayMenuRenderer.ApplyTo(_manager._contextMenu);
                }
            }

            if (_manager._contextMenu != null)
            {
                _manager._contextMenu.Refresh();
            }

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
                _manager._overlayWindow.ApplyMenuTheme(theme);
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
        if (_isShuttingDown || _overlayWindow == null)
            return;

        var rect = _overlayWindow.GetCurrentRect();
        if (rect == null)
            return;

        var (rectLeft, rectTop, width, height) = rect.Value;
        var (allMonitors, primaryMonitor) = GetMonitorsAndPrimary();

        _currentSettings = OverlayPositionCoordinator.SaveFromDragEnd(
            _currentSettings,
            rectLeft,
            rectTop,
            width,
            height,
            allMonitors,
            primaryMonitor);
        _settingsStore.TrySave(_currentSettings);

        // Sync the updated settings back to OverlayWindow so its internal state
        // matches. Custom mode no longer stores absolute pixels — the normalized
        // ratios drive all later DPI/display-change re-anchoring.
        _overlayWindow.UpdateSettings(_currentSettings);
        ReconcileOverlayAppearance();

        UpdateMenuCheckmarks();
    }

    private void OnOverlayCustomPositionSettingsUpdated(TraySettings updatedSettings)
    {
        if (_isShuttingDown)
            return;

        _currentSettings = updatedSettings;
        _settingsStore.TrySave(_currentSettings);
        _overlayWindow?.UpdateSettings(_currentSettings);
        ReconcileOverlayAppearance();
        UpdateMenuCheckmarks();
    }

    private (IReadOnlyList<MonitorInfo> All, MonitorInfo Primary) GetMonitorsAndPrimary()
    {
        var all = _workAreaProvider.GetAllMonitorInfos();
        var primaryWorkArea = _workAreaProvider.GetPrimaryWorkArea();
        MonitorInfo primary = new MonitorInfo(
            DeviceName: string.Empty,
            WorkingArea: primaryWorkArea,
            IsPrimary: true);

        foreach (var info in all)
        {
            if (info.IsPrimary)
            {
                primary = info;
                break;
            }
        }

        return (all, primary);
    }

    private void InitializeContextMenu()
    {
        _contextMenu = new ContextMenuStrip();

        var resolvedTheme = _themeCoordinator?.CurrentApplicationTheme ?? EffectiveTheme.Light;
        _trayMenuRenderer = new TrayMenuRenderer(resolvedTheme);
        _contextMenu.Font = new System.Drawing.Font("Microsoft YaHei UI", 9F);

        _metricWindowSubmenu.Text = Localization.MetricWindowMenu(_currentSettings.Language);
        foreach (MetricWindow window in Enum.GetValues<MetricWindow>())
        {
            var item = new ToolStripMenuItem(Localization.GetMetricWindowDisplayName(window, _currentSettings.Language));
            item.Click += (sender, e) => OnMetricWindowSelected(window);
            _windowMenuItems[window] = item;
            _metricWindowSubmenu.DropDownItems.Add(item);
        }
        _contextMenu.Items.Add(_metricWindowSubmenu);

        _refreshCadenceSubmenu.Text = Localization.RefreshCadenceMenu(_currentSettings.Language);
        foreach (RefreshCadence cadence in Enum.GetValues<RefreshCadence>())
        {
            var item = new ToolStripMenuItem(Localization.GetRefreshCadenceDisplayName(cadence, _currentSettings.Language));
            item.Click += (sender, e) => OnRefreshCadenceSelected(cadence);
            _cadenceMenuItems[cadence] = item;
            _refreshCadenceSubmenu.DropDownItems.Add(item);
        }
        _contextMenu.Items.Add(_refreshCadenceSubmenu);

        _dataSourceSubmenu.Text = Localization.DataSourceMenu(_currentSettings.Language);
        _windowsDataSourceMenuItem.Text = Localization.DataSourceWindows(_currentSettings.Language);
        _windowsDataSourceMenuItem.Click += OnWindowsDataSourceClicked;
        _dataSourceSubmenu.DropDownItems.Add(_windowsDataSourceMenuItem);
        _dataSourceSubmenu.DropDownOpening += (s, e) => DataSourceDiscoveryTask = OnDataSourceSubmenuOpeningAsync(s, e);
        _contextMenu.Items.Add(_dataSourceSubmenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        _openSessionsFolderMenuItem.Text = Localization.OpenSessionsFolderMenu(_currentSettings.Language);
        _openSessionsFolderMenuItem.Click += OnOpenSessionsFolderClicked;
        _contextMenu.Items.Add(_openSessionsFolderMenuItem);

        _launchAtLoginMenuItem.Text = Localization.LaunchAtLogin(_currentSettings.Language);
        _launchAtLoginMenuItem.Click += OnLaunchAtLoginClicked;
        _contextMenu.Items.Add(_launchAtLoginMenuItem);

        _contextMenu.Items.Add(new ToolStripSeparator());

        _overlaySubmenu.Text = Localization.OverlayMenu(_currentSettings.Language);
        _showOverlayMenuItem.Text = Localization.ShowOverlayMenu(_currentSettings.Language);
        _showOverlayMenuItem.Click += OnShowOverlayClicked;
        _overlaySubmenu.DropDownItems.Add(_showOverlayMenuItem);

        _lockOverlayMenuItem.Text = Localization.LockOverlayMenu(_currentSettings.Language);
        _lockOverlayMenuItem.Click += OnLockOverlayClicked;
        _overlaySubmenu.DropDownItems.Add(_lockOverlayMenuItem);

        _overlaySubmenu.DropDownItems.Add(new ToolStripSeparator());

        _overlayThemeSubmenu.Text = Localization.OverlayThemeMenu(_currentSettings.Language);
        foreach (var preference in OverlayMenuDefinition.ThemePreferences)
        {
            var item = new ToolStripMenuItem(OverlayMenuDefinition.GetThemeText(preference, _currentSettings.Language));
            item.Click += (sender, e) => OnOverlayThemeSelected(preference);
            _overlayThemeMenuItems[preference] = item;
            _overlayThemeSubmenu.DropDownItems.Add(item);
        }
        _overlaySubmenu.DropDownItems.Add(_overlayThemeSubmenu);

        _overlayOpacitySubmenu.Text = Localization.BackgroundOpacityMenu(_currentSettings.Language);
        foreach (var preference in OverlayMenuDefinition.OpacityPreferences)
        {
            var item = new ToolStripMenuItem(OverlayMenuDefinition.GetOpacityText(preference, _currentSettings.Language));
            item.Click += (sender, e) => OnOverlayOpacitySelected(preference);
            _overlayOpacityMenuItems[preference] = item;
            _overlayOpacitySubmenu.DropDownItems.Add(item);
        }
        _overlaySubmenu.DropDownItems.Add(_overlayOpacitySubmenu);

        _overlayPositionSubmenu.Text = Localization.PositionMenu(_currentSettings.Language);
        foreach (var preset in OverlayMenuDefinition.PositionPresets)
        {
            var item = new ToolStripMenuItem(OverlayMenuDefinition.GetPositionText(preset, _currentSettings.Language));
            item.Click += (sender, e) => OnOverlayPositionPresetSelected(preset);
            _overlayPositionMenuItems[preset] = item;
            _overlayPositionSubmenu.DropDownItems.Add(item);
        }
        _overlayPositionSubmenu.DropDownOpening += OnOverlayPositionSubmenuOpening;
        _overlaySubmenu.DropDownItems.Add(_overlayPositionSubmenu);

        _memorySubmenu.Text = Localization.MemoryMenu(_currentSettings.Language);

        _appearanceMemorySubmenu.Text = Localization.AppearanceMemoryMenu(_currentSettings.Language);
        foreach (var mode in OverlayMenuDefinition.AppearanceMemoryModes)
        {
            var item = new ToolStripMenuItem(OverlayMenuDefinition.GetAppearanceMemoryModeText(mode, _currentSettings.Language))
            {
                CheckOnClick = true
            };
            item.Click += (sender, e) => OnOverlayAppearanceMemoryModeSelected(mode);
            _appearanceMemoryMenuItems[mode] = item;
            _appearanceMemorySubmenu.DropDownItems.Add(item);
        }
        _memorySubmenu.DropDownItems.Add(_appearanceMemorySubmenu);

        _positionMemorySubmenu.Text = Localization.PositionMenu(_currentSettings.Language);
        foreach (var mode in OverlayMenuDefinition.PositionMemoryModes)
        {
            var item = new ToolStripMenuItem(OverlayMenuDefinition.GetPositionMemoryModeText(mode, _currentSettings.Language))
            {
                CheckOnClick = true
            };
            item.Click += (sender, e) => OnOverlayPositionMemoryModeSelected(mode);
            _positionMemoryMenuItems[mode] = item;
            _positionMemorySubmenu.DropDownItems.Add(item);
        }
        _memorySubmenu.DropDownItems.Add(_positionMemorySubmenu);

        _overlaySubmenu.DropDownItems.Add(_memorySubmenu);

        _contextMenu.Items.Add(_overlaySubmenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        _themeSubmenu.Text = Localization.ThemeMenu(_currentSettings.Language);
        foreach (ApplicationThemePreference preference in Enum.GetValues<ApplicationThemePreference>())
        {
            var item = new ToolStripMenuItem(Localization.GetApplicationThemeDisplayName(preference, _currentSettings.Language));
            item.Click += (sender, e) => OnApplicationThemeSelected(preference);
            _applicationThemeMenuItems[preference] = item;
            _themeSubmenu.DropDownItems.Add(item);
        }
        _contextMenu.Items.Add(_themeSubmenu);

        _languageSubmenu.Text = Localization.LanguageMenu(_currentSettings.Language);

        _englishLanguageMenuItem.Text = "English";
        _englishLanguageMenuItem.Click += (sender, e) => OnLanguageSelected(Language.English);
        _languageSubmenu.DropDownItems.Add(_englishLanguageMenuItem);

        _chineseLanguageMenuItem.Text = "简体中文";
        _chineseLanguageMenuItem.Click += (sender, e) => OnLanguageSelected(Language.Chinese);
        _languageSubmenu.DropDownItems.Add(_chineseLanguageMenuItem);

        _contextMenu.Items.Add(_languageSubmenu);

        _contextMenu.Items.Add(new ToolStripSeparator());

        _refreshMenuItem.Text = Localization.RefreshMenu(_currentSettings.Language);
        _refreshMenuItem.Click += OnRefreshClicked;
        _contextMenu.Items.Add(_refreshMenuItem);

        _exitMenuItem.Text = Localization.ExitMenu(_currentSettings.Language);
        _exitMenuItem.Click += OnExitClicked;
        _contextMenu.Items.Add(_exitMenuItem);

        // Apply renderer and compact layout to the root menu and all static
        // nested DropDowns after all items have been created.
        _trayMenuRenderer.ApplyTo(_contextMenu);

        _notifyIcon.ContextMenuStrip = _contextMenu;

        UpdateMenuCheckmarks();
    }

    private void OnShowOverlayClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        bool enabled = _showOverlayMenuItem.Checked;
        ToggleOverlayEnabled(enabled);
    }

    private void OnLockOverlayClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        bool locked = _lockOverlayMenuItem.Checked;
        ToggleOverlayLocked(locked);
    }

    private void OnOverlayPositionPresetSelected(OverlayPositionPreset preset)
    {
        if (_isShuttingDown || _overlayWindow == null)
            return;

        var rect = _overlayWindow.GetCurrentRect();
        if (rect == null)
            return;

        var (left, top, width, height) = rect.Value;

        var (allMonitors, primaryMonitor) = GetMonitorsAndPrimary();

        _currentSettings = OverlayPositionCoordinator.SaveFromPresetSelection(
            _currentSettings,
            preset,
            left,
            top,
            width,
            height,
            allMonitors,
            primaryMonitor);

        _overlayWindow.UpdateSettings(_currentSettings);

        var targetMonitor = OverlayPositionCalculator.FindBestMonitor(
            left, top, width, height, allMonitors, primaryMonitor);
        var size = new System.Windows.Size(width, height);
        Thickness dipMargin = new Thickness(16);
        Thickness physicalMargin = DpiHelper.ConvertDipMarginToPhysical(dipMargin, targetMonitor.DpiX, targetMonitor.DpiY);
        var (newLeft, newTop) = OverlayPositionCalculator.CalculatePresetPosition(
            preset, size, targetMonitor.WorkingArea, physicalMargin);

        _overlayWindow.MoveToPosition(newLeft, newTop);

        _settingsStore.TrySave(_currentSettings);
        UpdateMenuCheckmarks();
    }

    private void OnOverlayOpacitySelected(OverlayOpacityPreference preference)
    {
        if (_isShuttingDown)
            return;

        var targetMonitor = ResolveTargetMonitorForMenu();
        _currentSettings = OverlayAppearanceCoordinator.SaveFromOpacitySelection(
            _currentSettings, preference, targetMonitor);
        _settingsStore.TrySave(_currentSettings);

        ApplyOverlayAppearance();
        UpdateMenuCheckmarks();
    }

    private void OnOverlayPositionMemoryModeSelected(OverlayPositionMemoryMode mode)
    {
        if (_isShuttingDown)
            return;

        SelectOverlayPositionMemoryMode(mode);
    }

    private void OnOverlayAppearanceMemoryModeSelected(OverlayAppearanceMemoryMode mode)
    {
        if (_isShuttingDown)
            return;

        SelectOverlayAppearanceMemoryMode(mode);
    }

    private void OnOverlayPositionSubmenuOpening(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        bool overlayVisible = _overlayWindow?.IsVisible ?? false;
        OverlayPositionPreset? activePreset = ResolveActivePresetForMenu();
        foreach (var kvp in _overlayPositionMenuItems)
        {
            kvp.Value.Enabled = overlayVisible;
            kvp.Value.Checked = kvp.Key == activePreset;
        }
    }

    private OverlayPositionPreset? ResolveActivePresetForMenu()
    {
        if (_overlayWindow == null)
            return null;

        return OverlayPositionCoordinator.GetActivePreset(_currentSettings, ResolveTargetMonitorForMenu());
    }

    /// <summary>
    /// Resolves the physical monitor whose appearance/position state should drive
    /// menu checkmarks and appearance application. When the overlay window has a
    /// live rect, the monitor containing it is used. When the window is hidden or
    /// not yet created, the persisted OverlayTargetMonitorId is matched against
    /// live monitors; the primary monitor is the final deterministic fallback.
    /// This mirrors the position memory target resolution so appearance and
    /// position checkmarks always reflect the same display.
    /// </summary>
    private MonitorInfo ResolveTargetMonitorForMenu()
    {
        var (allMonitors, primaryMonitor) = GetMonitorsAndPrimary();

        var rect = _overlayWindow?.GetCurrentRect();
        if (rect != null)
        {
            var (left, top, width, height) = rect.Value;
            return OverlayPositionCalculator.FindBestMonitor(
                left, top, width, height, allMonitors, primaryMonitor);
        }

        string? targetId = _currentSettings.OverlayTargetMonitorId;
        if (!string.IsNullOrWhiteSpace(targetId))
        {
            foreach (MonitorInfo m in allMonitors)
            {
                if (!string.IsNullOrWhiteSpace(m.StableId)
                    && MonitorId.Equals(m.StableId, targetId))
                    return m;
            }
        }

        return primaryMonitor;
    }

    /// <summary>
    /// Restores the effective appearance for the current target monitor, syncing
    /// the scalar OverlayTheme/OverlayOpacity fields and persisting any change
    /// (e.g. a first-seen display inheriting its own record). Then applies the
    /// coordinated theme+opacity to the overlay window. Called on startup, drag
    /// end, display-config change, and position-settings updates.
    /// </summary>
    private void ReconcileOverlayAppearance()
    {
        if (_isShuttingDown || _overlayWindow == null)
            return;

        var targetMonitor = ResolveTargetMonitorForMenu();
        var updated = OverlayAppearanceCoordinator.RestoreForMonitor(_currentSettings, targetMonitor);
        if (updated != _currentSettings)
        {
            _currentSettings = updated;
            _settingsStore.TrySave(_currentSettings);
        }

        ApplyOverlayAppearance();
    }

    /// <summary>
    /// Applies the effective overlay theme and opacity as a single coordinated
    /// state. Reads the scalar OverlayTheme/OverlayOpacity (kept in sync by the
    /// appearance coordinator) and resolves the theme preference to an
    /// EffectiveTheme before calling ApplyAppearance on the window.
    /// </summary>
    private void ApplyOverlayAppearance()
    {
        if (_overlayWindow == null || _themeResolver == null)
            return;

        EffectiveTheme effectiveTheme = _themeResolver.ResolveOverlay(
            _currentSettings.OverlayTheme, _currentSettings.ApplicationTheme);
        _overlayWindow.ApplyAppearance(effectiveTheme, _currentSettings.OverlayOpacity);
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
            UpdateOverlayContextMenu();
            return;
        }

        _currentSettings = _currentSettings with { Language = language };
        _settingsStore.TrySave(_currentSettings);

        UpdateMenuLocalization();
        UpdateMenuCheckmarks();
        ApplySettingsToViewModelAndPanel();
        _overlayWindow?.UpdateSettings(_currentSettings);

        UpdateOverlayContent();
        UpdateOverlayContextMenu();

        if (_latestSnapshot.HasValue)
        {
            UpdateUI(_latestSnapshot.Value);
        }
    }

    private void OnWindowsDataSourceClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        LastDataSourceSwitchTask = SwitchDataSourceAsync(CodexDataSourceSelection.Windows);
    }

    private void OnWslDataSourceClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        if (sender is ToolStripMenuItem item && item.Tag is string distroName)
        {
            LastDataSourceSwitchTask = SwitchDataSourceAsync(CodexDataSourceSelection.ForWsl(distroName));
        }
    }

    internal void RaiseWindowsDataSourceClicked() => OnWindowsDataSourceClicked(null, EventArgs.Empty);
    internal void RaiseWslDataSourceClicked(string distroName)
    {
        if (_wslDataSourceMenuItems.TryGetValue(distroName, out var item))
        {
            OnWslDataSourceClicked(item, EventArgs.Empty);
        }
    }
    internal void RaiseDataSourceSubmenuOpening() => DataSourceDiscoveryTask = OnDataSourceSubmenuOpeningAsync(null, EventArgs.Empty);
    internal void RaiseOpenSessionsFolderClicked() => OnOpenSessionsFolderClicked(null, EventArgs.Empty);

    private async Task OnDataSourceSubmenuOpeningAsync(object? sender, EventArgs e)
    {
        if (_isShuttingDown)
            return;

        if (Interlocked.CompareExchange(ref _isDiscoveringWsl, 1, 0) != 0)
            return;

        try
        {
            bool uiCancelled = false;
            _uiDispatcher.Invoke(() =>
            {
                if (_isShuttingDown)
                {
                    uiCancelled = true;
                    return;
                }

                ClearWslDataSourceMenuItems();

                var detectingMenuItem = new ToolStripMenuItem(Localization.DataSourceDetectingWsl(_currentSettings.Language))
                {
                    Enabled = false
                };
                _trayMenuRenderer?.ConfigureDynamicItem(detectingMenuItem);
                _dataSourceSubmenu.DropDownItems.Add(detectingMenuItem);
            });

            if (uiCancelled)
                return;

            var discovered = await _wslDiscovery.DiscoverAsync(_shutdownToken)
                .ConfigureAwait(false);

            _discoveredWslSources = discovered;

            _uiDispatcher.Invoke(() =>
            {
                if (!_isShuttingDown)
                    PopulateWslDataSourceMenu();
            });
        }
        catch (OperationCanceledException)
        {
            _discoveredWslSources = Array.Empty<ResolvedCodexDataSource>();
            _uiDispatcher.Invoke(() =>
            {
                if (!_isShuttingDown)
                    PopulateWslDataSourceMenu();
            });
        }
        catch
        {
            _discoveredWslSources = Array.Empty<ResolvedCodexDataSource>();
            _uiDispatcher.Invoke(() =>
            {
                if (!_isShuttingDown)
                    PopulateWslDataSourceMenu();
            });
        }
        finally
        {
            Interlocked.Exchange(ref _isDiscoveringWsl, 0);
        }
    }

    private void ClearWslDataSourceMenuItems()
    {
        var itemsToRemove = new List<ToolStripMenuItem>();
        foreach (var item in _dataSourceSubmenu.DropDownItems)
        {
            if (item is ToolStripMenuItem menuItem && menuItem != _windowsDataSourceMenuItem)
            {
                itemsToRemove.Add(menuItem);
            }
        }
        foreach (var item in itemsToRemove)
        {
            _dataSourceSubmenu.DropDownItems.Remove(item);
            item.Click -= OnWslDataSourceClicked;
            item.Dispose();
        }
        _wslDataSourceMenuItems.Clear();
    }

    private void PopulateWslDataSourceMenu()
    {
        ClearWslDataSourceMenuItems();

        var discovered = _discoveredWslSources ?? Array.Empty<ResolvedCodexDataSource>();
        var savedWslDistro = _currentSettings.DataSource.Kind == CodexDataSourceKind.Wsl
            ? _currentSettings.DataSource.WslDistributionName
            : null;

        bool hasDiscoveredSources = discovered.Count > 0;
        bool hasSavedWsl = !string.IsNullOrEmpty(savedWslDistro);

        if (!hasDiscoveredSources && !hasSavedWsl)
        {
            var noWslMenuItem = new ToolStripMenuItem(Localization.DataSourceNoWsl(_currentSettings.Language))
            {
                Enabled = false
            };
            _trayMenuRenderer?.ConfigureDynamicItem(noWslMenuItem);
            _dataSourceSubmenu.DropDownItems.Add(noWslMenuItem);
            UpdateDataSourceMenuCheckmarks();
            return;
        }

        foreach (var source in discovered.OrderBy(s => s.DisplayName))
        {
            var item = new ToolStripMenuItem(source.DisplayName);
            item.Tag = source.Selection.WslDistributionName;
            item.Click += OnWslDataSourceClicked;
            _trayMenuRenderer?.ConfigureDynamicItem(item);
            _wslDataSourceMenuItems[source.Selection.WslDistributionName!] = item;
            _dataSourceSubmenu.DropDownItems.Add(item);
        }

        if (hasSavedWsl && !_wslDataSourceMenuItems.ContainsKey(savedWslDistro!))
        {
            var item = new ToolStripMenuItem($"WSL: {savedWslDistro}");
            item.Tag = savedWslDistro;
            item.Click += OnWslDataSourceClicked;
            _trayMenuRenderer?.ConfigureDynamicItem(item);
            _wslDataSourceMenuItems[savedWslDistro!] = item;
            _dataSourceSubmenu.DropDownItems.Add(item);
        }

        UpdateDataSourceMenuCheckmarks();

        if (_isSwitchingDataSource)
        {
            foreach (var kvp in _wslDataSourceMenuItems)
            {
                kvp.Value.Enabled = false;
            }
        }
    }

    private void UpdateDataSourceMenuCheckmarks()
    {
        _windowsDataSourceMenuItem.Checked = _currentSettings.DataSource.Kind == CodexDataSourceKind.Windows;

        foreach (var kvp in _wslDataSourceMenuItems)
        {
            kvp.Value.Checked = _currentSettings.DataSource.Kind == CodexDataSourceKind.Wsl
                && _currentSettings.DataSource.WslDistributionName == kvp.Key;
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

        _overlayPositionSubmenu.Text = Localization.PositionMenu(language);
        foreach (var kvp in _overlayPositionMenuItems)
        {
            kvp.Value.Text = Localization.GetPositionPresetDisplayName(kvp.Key, language);
        }

        _memorySubmenu.Text = Localization.MemoryMenu(language);

        _appearanceMemorySubmenu.Text = Localization.AppearanceMemoryMenu(language);
        foreach (var kvp in _appearanceMemoryMenuItems)
        {
            kvp.Value.Text = OverlayMenuDefinition.GetAppearanceMemoryModeText(kvp.Key, language);
        }

        _positionMemorySubmenu.Text = Localization.PositionMenu(language);
        foreach (var kvp in _positionMemoryMenuItems)
        {
            kvp.Value.Text = OverlayMenuDefinition.GetPositionMemoryModeText(kvp.Key, language);
        }

        _overlayOpacitySubmenu.Text = Localization.BackgroundOpacityMenu(language);
        foreach (var kvp in _overlayOpacityMenuItems)
        {
            kvp.Value.Text = Localization.GetOverlayOpacityDisplayName(kvp.Key, language);
        }

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

        _dataSourceSubmenu.Text = Localization.DataSourceMenu(language);
        _windowsDataSourceMenuItem.Text = Localization.DataSourceWindows(language);
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
            if (_isShuttingDown || !_isScannerReady)
                return;

            if (!await _refreshSemaphore.WaitAsync(0, _shutdownToken))
                return;

            try
            {
                await ScanAndUpdateCoreAsync(_shutdownToken).ConfigureAwait(false);
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
                _uiDispatcher.BeginInvoke(() => _monitorPanel?.SetIsRefreshing(false));
            }
        }
    }

    private async Task ScanAndUpdateCoreAsync(CancellationToken cancellationToken)
    {
        if (_isShuttingDown)
            return;

        bool shouldRefresh = false;
        _uiDispatcher.Invoke(() =>
        {
            if (!_isShuttingDown)
            {
                _monitorPanel?.SetIsRefreshing(true);
                shouldRefresh = true;
            }
        });

        if (!shouldRefresh)
            return;

        try
        {
            var snapshot = await Task.Run(() => _sessionScanner.Refresh(DateTimeOffset.UtcNow), cancellationToken)
                .ConfigureAwait(false);

            _uiDispatcher.Invoke(() =>
            {
                if (_isShuttingDown)
                    return;

                _latestSnapshot = snapshot;
                UpdateUI(snapshot);
                _overlayLifecycle?.EnsureVisible(_currentSettings);
                UpdateOverlayContent();
                _monitorPanel?.UpdateSnapshot(snapshot);
            });
        }
        finally
        {
            _uiDispatcher.Invoke(() =>
            {
                if (!_isShuttingDown)
                    _monitorPanel?.SetIsRefreshing(false);
            });
        }
    }

    private async Task SwitchDataSourceAsync(CodexDataSourceSelection selection)
    {
        if (_isShuttingDown)
            return;

        bool acquiredSwitchSemaphore = false;
        try
        {
            acquiredSwitchSemaphore = await _switchSemaphore.WaitAsync(0, _shutdownToken).ConfigureAwait(false);
            if (!acquiredSwitchSemaphore)
            {
                OnSwitchRejected?.Invoke();
                return;
            }

            bool isSameSelection = _currentSettings.DataSource.Equals(selection);
            if (isSameSelection && _isScannerReady)
            {
                _uiDispatcher.Invoke(() =>
                {
                    if (!_isShuttingDown)
                        UpdateDataSourceMenuCheckmarks();
                });
                return;
            }

            _uiDispatcher.Invoke(() =>
            {
                if (!_isShuttingDown)
                    DisableDataSourceMenuItems();
            });

            var resolved = await _dataSourceResolver.ResolveAsync(selection, _shutdownToken).ConfigureAwait(false);

            if (resolved == null)
            {
                _uiDispatcher.Invoke(() =>
                {
                    if (_isShuttingDown)
                        return;
                    ShowBalloonTip(Localization.DataSourceSwitchFailed(_currentSettings.Language), ToolTipIcon.Warning);
                    UpdateDataSourceMenuCheckmarks();
                    EnableDataSourceMenuItems();
                });
                return;
            }

            OnSwitchAcquiringRefreshSemaphore?.Invoke();
            await _refreshSemaphore.WaitAsync(_shutdownToken).ConfigureAwait(false);

            try
            {
                if (_isShuttingDown)
                    return;

                var newScanner = new SessionScanner(resolved.CodexHome);
                bool commitSuccess = false;

                _uiDispatcher.Invoke(() =>
                {
                    if (_isShuttingDown)
                        return;

                    // Snapshot _currentSettings on the UI thread immediately before
                    // saving, so any concurrent non-data-source setting changes
                    // (language, theme, cadence, etc.) that occurred while resolve
                    // was in flight are preserved alongside the new DataSource.
                    var newSettings = _currentSettings with { DataSource = selection };

                    if (!_settingsStore.TrySave(newSettings))
                    {
                        ShowBalloonTip(Localization.DataSourceSaveFailed(_currentSettings.Language), ToolTipIcon.Warning);
                        return;
                    }

                    _sessionScanner = newScanner;
                    _currentSettings = newSettings;
                    _latestSnapshot = null;
                    _isScannerReady = true;
                    if (!_refreshTimer.IsEnabled)
                    {
                        _refreshTimer.Start();
                    }
                    commitSuccess = true;
                });

                if (!commitSuccess)
                {
                    _uiDispatcher.Invoke(() =>
                    {
                        if (!_isShuttingDown)
                        {
                            UpdateDataSourceMenuCheckmarks();
                            EnableDataSourceMenuItems();
                        }
                    });
                    return;
                }

                try
                {
                    await ScanAndUpdateCoreAsync(_shutdownToken).ConfigureAwait(false);
                }
                catch
                {
                    // The data source has already been committed. A refresh failure
                    // after commit is a read failure, not a switch failure; leave the
                    // new source active and let the normal UI path show the error state.
                }
            }
            finally
            {
                _refreshSemaphore.Release();
                _uiDispatcher.Invoke(() =>
                {
                    if (!_isShuttingDown)
                    {
                        UpdateDataSourceMenuCheckmarks();
                        EnableDataSourceMenuItems();
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            _uiDispatcher.Invoke(() =>
            {
                if (!_isShuttingDown)
                {
                    UpdateDataSourceMenuCheckmarks();
                    EnableDataSourceMenuItems();
                }
            });
        }
        catch
        {
            _uiDispatcher.Invoke(() =>
            {
                if (!_isShuttingDown)
                {
                    ShowBalloonTip(Localization.DataSourceSwitchFailed(_currentSettings.Language), ToolTipIcon.Warning);
                    UpdateDataSourceMenuCheckmarks();
                    EnableDataSourceMenuItems();
                }
            });
        }
        finally
        {
            if (acquiredSwitchSemaphore)
            {
                _switchSemaphore.Release();
            }
        }
    }

    private void DisableDataSourceMenuItems()
    {
        if (_isShuttingDown)
            return;

        _isSwitchingDataSource = true;
        _windowsDataSourceMenuItem.Enabled = false;
        foreach (var item in _wslDataSourceMenuItems.Values)
        {
            item.Enabled = false;
        }
    }

    private void EnableDataSourceMenuItems()
    {
        if (_isShuttingDown)
            return;

        _isSwitchingDataSource = false;
        _windowsDataSourceMenuItem.Enabled = true;
        foreach (var item in _wslDataSourceMenuItems.Values)
        {
            item.Enabled = true;
        }
    }

    private void UpdateUI(UsageSnapshot snapshot)
    {
        if (_isShuttingDown)
            return;

        string tooltip = TrayTextFormatter.FormatTooltip(snapshot, _currentSettings.SelectedWindow, _currentSettings.Language);
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

        var targetMonitor = ResolveTargetMonitorForMenu();
        _currentSettings = OverlayAppearanceCoordinator.SaveFromThemeSelection(
            _currentSettings, preference, targetMonitor);
        _settingsStore.TrySave(_currentSettings);

        ApplyOverlayAppearance();
        UpdateMenuCheckmarks();
    }

    private void ApplySettingsToViewModelAndPanel()
    {
        _settingsSynchronizer?.Apply(_currentSettings);
    }

    private void OnOpenSessionsFolderClicked(object? sender, EventArgs e)
    {
        if (_isShuttingDown || !_isScannerReady)
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

        bool overlayVisible = _overlayWindow?.IsVisible ?? false;
        OverlayPositionPreset? activePreset = ResolveActivePresetForMenu();
        foreach (var kvp in _overlayPositionMenuItems)
        {
            kvp.Value.Enabled = overlayVisible;
            kvp.Value.Checked = kvp.Key == activePreset;
        }

        foreach (var kvp in _positionMemoryMenuItems)
        {
            kvp.Value.Enabled = overlayVisible;
            kvp.Value.Checked = kvp.Key == _currentSettings.PositionMemoryMode;
        }

        foreach (var kvp in _appearanceMemoryMenuItems)
        {
            kvp.Value.Enabled = overlayVisible;
            kvp.Value.Checked = kvp.Key == _currentSettings.AppearanceMemoryMode;
        }

        foreach (var kvp in _overlayOpacityMenuItems)
        {
            kvp.Value.Checked = kvp.Key == _currentSettings.OverlayOpacity;
        }

        UpdateDataSourceMenuCheckmarks();
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

        _shutdownCts.Cancel();

        _overlayRecoveryCoordinator?.PrepareForShutdown();
        _systemThemeChangeCoordinator?.PrepareForShutdown();

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
            _isShuttingDown = true;
            _shutdownCts.Cancel();

            _refreshTimer.Stop();

            _overlayRecoveryCoordinator?.PrepareForShutdown();
            _overlayRecoveryCoordinator?.Dispose();
            _overlayRecoveryCoordinator = null;

            _systemThemeChangeCoordinator?.PrepareForShutdown();
            _systemThemeChangeCoordinator?.Dispose();
            _systemThemeChangeCoordinator = null;

            _overlayLifecycle?.PrepareForShutdown();
            _overlayLifecycle = null;

            _overlayWindow?.PrepareForShutdown();
            _overlayWindow = null;

            _monitorPanel?.PrepareForShutdown();
            _monitorPanel = null;

            _notifyIcon.Visible = false;
            _notifyIcon.ContextMenuStrip = null;
            _contextMenu?.Dispose();
            _contextMenu = null;

            _notifyIcon.Icon = null;
            _notifyIcon.Dispose();
            _trayIcon?.Dispose();
        }
    }
}
