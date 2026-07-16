using System;
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
    private readonly ToolStripMenuItem _tpsMenuItem;
    private readonly ToolStripMenuItem _statusMenuItem;
    private bool _isShuttingDown;
    private bool _isDisposed;

    public TrayIconManager()
    {
        _notifyIcon = new NotifyIcon();
        _sessionScanner = new SessionScanner();
        _refreshTimer = new DispatcherTimer();
        _refreshTimer.Interval = TimeSpan.FromSeconds(5);
        _refreshTimer.Tick += OnRefreshTimerTick;
        _refreshSemaphore = new SemaphoreSlim(1, 1);
        _tpsMenuItem = new ToolStripMenuItem { Enabled = false };
        _statusMenuItem = new ToolStripMenuItem { Enabled = false };
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

        contextMenu.Items.Add(_tpsMenuItem);
        contextMenu.Items.Add(_statusMenuItem);

        contextMenu.Items.Add(new ToolStripSeparator());

        var refreshMenuItem = new ToolStripMenuItem("Refresh");
        refreshMenuItem.Click += OnRefreshClicked;
        contextMenu.Items.Add(refreshMenuItem);

        var exitMenuItem = new ToolStripMenuItem("Exit");
        exitMenuItem.Click += OnExitClicked;
        contextMenu.Items.Add(exitMenuItem);

        _notifyIcon.ContextMenuStrip = contextMenu;
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

        double tps = snapshot.OneMinute.TokensPerSecond;
        string statusText = snapshot.Status switch
        {
            CollectionStatus.Ready => "Ready",
            CollectionStatus.SessionsDirectoryMissing => "No sessions",
            CollectionStatus.ReadFailed => "Error",
            _ => "Unknown"
        };

        _tpsMenuItem.Text = $"TPS: {tps:F1}/s";
        _statusMenuItem.Text = $"Status: {statusText}";

        string tooltip = $"Codex TPS\nTPS: {tps:F1}/s\nStatus: {statusText}\nActive Sessions: {snapshot.ActiveSessions}";
        string trayText = tps >= 1 ? $"TPS: {tps:F0}/s" : "No activity";

        _notifyIcon.Text = tooltip;
        _notifyIcon.BalloonTipText = trayText;
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