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

    private UsageSnapshot? _latestSnapshot;
    private TraySettings _currentSettings;
    private bool _isShuttingDown;
    private bool _isDisposed;

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

    public TrayIconManager() : this(TraySettingsStore.CreateDefault())
    {
    }

    public TrayIconManager(TraySettingsStore settingsStore)
    {
        _settingsStore = settingsStore;
        _currentSettings = settingsStore.Load();

        _notifyIcon = new NotifyIcon();
        _sessionScanner = new SessionScanner();
        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Interval = _currentSettings.RefreshCadence.ToTimeSpan();
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshSemaphore = new SemaphoreSlim(1, 1);
    }

    public void Start()
    {
        InitializeContextMenu();
        InitializeNotifyIcon();

        _refreshTimer.Start();
        _ = RefreshAsync();
    }

    private void InitializeContextMenu()
    {
        var contextMenu = new ContextMenuStrip();

        var metricsSubmenu = new ToolStripMenuItem("Metrics");
        metricsSubmenu.DropDownItems.Add(_totalTpsMenuItem);
        metricsSubmenu.DropDownItems.Add(_inputTpsMenuItem);
        metricsSubmenu.DropDownItems.Add(_cachedTpsMenuItem);
        metricsSubmenu.DropDownItems.Add(_outputTpsMenuItem);
        metricsSubmenu.DropDownItems.Add(_reasoningTpsMenuItem);
        metricsSubmenu.DropDownItems.Add(_requestsMenuItem);
        metricsSubmenu.DropDownItems.Add(_activeSessionsMenuItem);
        metricsSubmenu.DropDownItems.Add(_cacheRatioMenuItem);
        metricsSubmenu.DropDownItems.Add(_statusMenuItem);
        contextMenu.Items.Add(metricsSubmenu);

        contextMenu.Items.Add(new ToolStripSeparator());

        var windowSubmenu = new ToolStripMenuItem("Metric Window");
        foreach (MetricWindow window in Enum.GetValues<MetricWindow>())
        {
            var item = new ToolStripMenuItem(window.GetDisplayName());
            item.Click += (sender, e) => OnMetricWindowSelected(window);
            _windowMenuItems[window] = item;
            windowSubmenu.DropDownItems.Add(item);
        }
        contextMenu.Items.Add(windowSubmenu);

        var cadenceSubmenu = new ToolStripMenuItem("Refresh Cadence");
        foreach (RefreshCadence cadence in Enum.GetValues<RefreshCadence>())
        {
            var item = new ToolStripMenuItem(cadence.GetDisplayName());
            item.Click += (sender, e) => OnRefreshCadenceSelected(cadence);
            _cadenceMenuItems[cadence] = item;
            cadenceSubmenu.DropDownItems.Add(item);
        }
        contextMenu.Items.Add(cadenceSubmenu);

        contextMenu.Items.Add(new ToolStripSeparator());

        var refreshMenuItem = new ToolStripMenuItem("Refresh");
        refreshMenuItem.Click += OnRefreshClicked;
        contextMenu.Items.Add(refreshMenuItem);

        var exitMenuItem = new ToolStripMenuItem("Exit");
        exitMenuItem.Click += OnExitClicked;
        contextMenu.Items.Add(exitMenuItem);

        _notifyIcon.ContextMenuStrip = contextMenu;

        UpdateMenuCheckmarks();
    }

    private void InitializeNotifyIcon()
    {
        _notifyIcon.Text = "Codex TPS";
        _notifyIcon.Icon = SystemIcons.Application;
        _notifyIcon.Visible = true;
        _notifyIcon.DoubleClick += OnRefreshClicked;
    }

    private void OnRefreshTimerTick(object? sender, EventArgs e)
    {
        _ = RefreshAsync();
    }

    private void OnRefreshClicked(object? sender, EventArgs e)
    {
        _ = RefreshAsync();
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

                var snapshot = await Task.Run(() => _sessionScanner.Refresh(DateTimeOffset.UtcNow));

                if (_isShuttingDown)
                    return;

                _latestSnapshot = snapshot;
                UpdateUI(snapshot);
            }
            finally
            {
                _refreshSemaphore.Release();
            }
        }
        catch
        {
        }
    }

    private void UpdateUI(UsageSnapshot snapshot)
    {
        if (_isShuttingDown)
            return;

        var metrics = _currentSettings.SelectedWindow.GetMetrics(snapshot);

        _totalTpsMenuItem.Text = $"Total: {metrics.TokensPerSecond:F1} token/s";
        _inputTpsMenuItem.Text = $"Input: {metrics.InputTokensPerSecond:F1} token/s";
        _cachedTpsMenuItem.Text = $"Cached: {metrics.CachedInputTokensPerSecond:F1} token/s";
        _outputTpsMenuItem.Text = $"Output: {metrics.OutputTokensPerSecond:F1} token/s";
        _reasoningTpsMenuItem.Text = $"Reasoning: {metrics.ReasoningTokensPerSecond:F1} token/s";
        _requestsMenuItem.Text = $"Requests: {metrics.RequestsPerMinute:F1}/min";
        _activeSessionsMenuItem.Text = $"Active Sessions: {snapshot.ActiveSessions}";
        _cacheRatioMenuItem.Text = $"Cache Ratio: {metrics.CacheRatio:P0}";
        _statusMenuItem.Text = $"Status: {TrayTextFormatter.GetStatusText(snapshot.Status)}";

        string tooltip = TrayTextFormatter.FormatTooltip(snapshot, _currentSettings.SelectedWindow);
        _notifyIcon.Text = tooltip;
    }

    private void OnMetricWindowSelected(MetricWindow window)
    {
        if (_isShuttingDown || window == _currentSettings.SelectedWindow)
            return;

        _currentSettings = _currentSettings with { SelectedWindow = window };
        _settingsStore.TrySave(_currentSettings);

        UpdateMenuCheckmarks();

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

        _refreshTimer.Stop();
        _refreshTimer.Interval = cadence.ToTimeSpan();
        _refreshTimer.Start();
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

            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }
    }
}