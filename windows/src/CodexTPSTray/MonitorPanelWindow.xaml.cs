using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using CodexTPSCore;

namespace CodexTPSTray;

public partial class MonitorPanelWindow : Window
{
    private readonly MonitorPanelViewModel _viewModel;
    private bool _isSynchronizingControls;
    private bool _isShuttingDown;
    private bool _shutdownPrepared;
    private HwndSource? _hwndSource;
    private readonly DpiRepositionGuard _dpiGuard = new();

    /// <summary>
    /// Fixed logical width of the panel in DIP. The panel must never widen due
    /// to a DPI transition; <see cref="NormalizePanelLayout"/> re-asserts this.
    /// </summary>
    internal const double PanelLogicalWidth = 390.0;

    /// <summary>
    /// Number of times <see cref="NormalizePanelLayout"/> has performed a full
    /// layout pass. Tests use this to verify DPI-callback dedup and ordering.
    /// </summary>
    internal int NormalizePanelLayoutCallCount { get; private set; }

    /// <summary>
    /// Resolves a physical screen point to a Screen. Injectable for tests so
    /// they can verify DPI reposition uses the window center, not Cursor.Position.
    /// </summary>
    internal Func<System.Drawing.Point, Screen?> ScreenFromPointResolver { get; set; } = p => Screen.FromPoint(p);

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOZORDER = 0x0004;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public event Action? RefreshRequested;
    public event Action? OpenFolderRequested;
    public event Action<MetricWindow>? MetricWindowChanged;
    public event Action<RefreshCadence>? RefreshCadenceChanged;

    public MonitorPanelWindow(MonitorPanelViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        PanelThemeResources.Apply(Resources, EffectiveTheme.Light);

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (_hwndSource != null)
        {
            _hwndSource.DpiChanged += OnHwndSourceDpiChanged;
        }
    }

    private void OnHwndSourceDpiChanged(object sender, System.Windows.HwndDpiChangedEventArgs e)
    {
        // Let WPF handle native PerMonitorV2 DPI re-layout automatically.
        // Do not set e.Handled or apply ScaleTransform (avoids double scaling).
        if (_isShuttingDown)
            return;

        long captured = _dpiGuard.Queue();
        // Render priority: WPF has finished its PerMonitorV2 DPI transform and
        // layout pass, so re-asserting the logical width and updating layout
        // here resyncs the physical HWND to 390 DIP at the new DPI before we
        // reposition. The guard ensures only the latest DPI callback runs.
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (!_dpiGuard.IsCurrent(captured))
                return;

            ExecuteDpiReposition();
        }));
    }

    /// <summary>
    /// Executes the DPI reposition logic. Extracted as an internal method so tests
    /// can verify behavior without relying on real DPI events or reflection.
    /// </summary>
    internal void ExecuteDpiReposition()
    {
        if (_isShuttingDown || !IsLoaded || !IsVisible)
            return;

        ExecuteDpiRepositionCore();
    }

    /// <summary>
    /// Core DPI reposition logic without visibility/shutdown guards. Tests call
    /// this directly to verify layout normalization + reposition ordering without
    /// needing a real DPI event or a shown window.
    /// </summary>
    internal void ExecuteDpiRepositionCore()
    {
        // Re-assert the fixed logical width and resync the physical HWND at the
        // current DPI before repositioning. This repairs the occasional width
        // drift left by WPF's PerMonitorV2 DPI transition.
        NormalizePanelLayout();

        // On DPI change for an already-visible window: stay on the monitor the
        // window is currently on (by window rect), do NOT jump to cursor screen.
        RepositionToCurrentMonitor();
    }

    /// <summary>
    /// Re-asserts the fixed 390 DIP logical width, discards cached measure/arrange
    /// state from a previous DPI, runs a layout pass at the current window DPI,
    /// and re-fetches the physical window rect. Extracted as internal for
    /// testability. Call this after WPF has completed its PerMonitorV2 DPI layout
    /// pass (e.g. from a Render-priority Dispatcher callback) or when re-showing a
    /// panel that may have missed a DPI change while hidden. This method does NOT
    /// reposition the window; it only normalizes its size and layout.
    /// </summary>
    /// <returns>
    /// The physical window rect at the current DPI, or <c>default</c> if the
    /// HWND is unavailable.
    /// </returns>
    internal RECT NormalizePanelLayout()
    {
        if (_isShuttingDown || !IsInitialized)
            return default;

        NormalizePanelLayoutCallCount++;

        // 1. Re-assert the fixed logical width. SizeToContent stays height-only so
        //    a DPI transition can never widen the panel.
        Width = PanelLogicalWidth;
        SizeToContent = SizeToContent.Height;

        // 2. Invalidate cached measure/arrange from the previous DPI so WPF
        //    recomputes the visual tree at the current DPI.
        InvalidateMeasure();
        InvalidateArrange();

        // 3. Force a synchronous layout pass. We are invoked from a Render-priority
        //    Dispatcher callback (DPI change) or synchronously on ShowNearTray, so
        //    WPF's own DPI layout pass has already completed. This resyncs the
        //    physical HWND size to 390 DIP at the current DPI.
        UpdateLayout();

        // 4. Re-fetch the physical window rect at the current window DPI.
        var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource == null)
            return default;

        IntPtr hwnd = hwndSource.Handle;
        if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out RECT rect))
            return default;

        return rect;
    }

    /// <summary>
    /// Internal for tests: queues a DPI-changed generation token. Mirrors the
    /// first half of <see cref="OnHwndSourceDpiChanged"/> without requiring a
    /// real DPI event.
    /// </summary>
    internal long QueueDpiChangedGeneration() => _dpiGuard.Queue();

    /// <summary>
    /// Internal for tests: runs the DPI-changed dispatch work for a given
    /// generation token. Returns <c>true</c> if the work ran (token was
    /// current), <c>false</c> if it was deduped by a newer generation. Mirrors
    /// the dispatched callback in <see cref="OnHwndSourceDpiChanged"/> without
    /// requiring a Dispatcher pump.
    /// </summary>
    internal bool RunDpiChangedDispatch(long capturedGeneration)
    {
        if (!_dpiGuard.IsCurrent(capturedGeneration))
            return false;

        ExecuteDpiRepositionCore();
        return true;
    }

    /// <summary>
    /// Internal for tests: invalidates pending DPI generation tokens, mirroring
    /// the hide path in <see cref="OnIsVisibleChanged"/>.
    /// </summary>
    internal void InvalidateDpiGeneration() => _dpiGuard.Invalidate();

    internal void ApplyTheme(EffectiveTheme theme)
    {
        PanelThemeResources.Apply(Resources, theme);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateWindowButtonStates();
        UpdateCadenceComboSelection();
        UpdateAllValues();
        UpdateStatusDotColor(_viewModel.StatusPresentationState.DotColor);
        KeyDown += OnKeyDown;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // When window hides, invalidate any pending DPI reposition so a queued
        // callback from before the hide cannot reposition the window after a
        // subsequent Show.
        if (!(bool)e.NewValue)
        {
            _dpiGuard.Invalidate();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _dpiGuard.Invalidate();
        if (_hwndSource != null)
        {
            _hwndSource.DpiChanged -= OnHwndSourceDpiChanged;
            _hwndSource = null;
        }
    }

    private void OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Hide();
        }
    }

    public void ShowNearTray()
    {
        // Invalidate any DPI callback queued while the panel was hidden so a
        // stale Dispatcher callback cannot overwrite the layout we are about to
        // normalize. The synchronous normalization below is authoritative for
        // the current DPI; a fresh DPI event after this point queues a new,
        // current generation and runs normally.
        _dpiGuard.Invalidate();

        double originalOpacity = Opacity;
        Opacity = 0;

        Show();

        try
        {
            // DPI may have changed while the panel was hidden. Re-assert the
            // fixed logical width and resync the physical HWND at the current
            // DPI before positioning, so the physical rect used for positioning
            // is correct.
            NormalizePanelLayout();

            // Initial show: open near the tray on the screen where the cursor currently is.
            PositionNearCursorScreen();
        }
        finally
        {
            Opacity = originalOpacity;
            Activate();
        }
    }

    private void RepositionToCurrentMonitor()
    {
        try
        {
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource == null)
                return;

            IntPtr hwnd = hwndSource.Handle;
            if (!GetWindowRect(hwnd, out RECT rect))
                return;

            int windowWidth = rect.Right - rect.Left;
            int windowHeight = rect.Bottom - rect.Top;
            if (windowWidth <= 0 || windowHeight <= 0)
                return;

            // Find the screen that contains the window center (most reliable for already-visible windows).
            Screen? screen = ResolveScreenFromWindowRect(rect);

            if (screen == null)
                screen = Screen.PrimaryScreen;
            if (screen == null)
                return;

            PositionWindowToScreenBottomRight(hwnd, screen, windowWidth, windowHeight);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Resolves which screen the window currently belongs to, using the window
    /// center point (NOT Cursor.Position). Extracted as internal for testability.
    /// </summary>
    internal Screen? ResolveScreenFromWindowRect(RECT rect)
    {
        int windowWidth = rect.Right - rect.Left;
        int windowHeight = rect.Bottom - rect.Top;
        if (windowWidth <= 0 || windowHeight <= 0)
            return null;

        int centerX = (rect.Left + rect.Right) / 2;
        int centerY = (rect.Top + rect.Bottom) / 2;
        return ScreenFromPointResolver(new System.Drawing.Point(centerX, centerY));
    }

    private void PositionNearCursorScreen()
    {
        try
        {
            var cursorPos = System.Windows.Forms.Cursor.Position;
            Screen? screen = Screen.FromPoint(cursorPos);

            if (screen == null)
                screen = Screen.PrimaryScreen;

            if (screen != null)
            {
                var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
                if (hwndSource != null)
                {
                    IntPtr hwnd = hwndSource.Handle;
                    if (GetWindowRect(hwnd, out RECT rect))
                    {
                        int windowWidth = rect.Right - rect.Left;
                        int windowHeight = rect.Bottom - rect.Top;
                        PositionWindowToScreenBottomRight(hwnd, screen, windowWidth, windowHeight);
                        return;
                    }
                }
            }
        }
        catch
        {
        }

        // Fallback: use primary screen
        try
        {
            Screen? primaryScreen = Screen.PrimaryScreen;
            if (primaryScreen == null)
                return;

            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource == null)
                return;

            IntPtr hwnd = hwndSource.Handle;
            if (!GetWindowRect(hwnd, out RECT rect))
                return;

            int windowWidth = rect.Right - rect.Left;
            int windowHeight = rect.Bottom - rect.Top;
            PositionWindowToScreenBottomRight(hwnd, primaryScreen, windowWidth, windowHeight);
        }
        catch
        {
        }
    }

    private void PositionWindowToScreenBottomRight(IntPtr hwnd, Screen screen, int windowWidth, int windowHeight)
    {
        var workingArea = screen.WorkingArea;
        double dpi = DpiHelper.GetDpiForWindow(hwnd);
        int physicalMargin = (int)Math.Round(DpiHelper.ConvertDipToPhysicalPixels(16, dpi));

        int desiredX = workingArea.Right - windowWidth - physicalMargin;
        int desiredY = workingArea.Bottom - windowHeight - physicalMargin;

        desiredX = Math.Max(workingArea.Left, desiredX);
        desiredY = Math.Max(workingArea.Top, desiredY);

        SetWindowPos(hwnd, IntPtr.Zero, desiredX, desiredY, 0, 0, SWP_NOSIZE | SWP_NOZORDER);
    }

    public void PrepareForShutdown()
    {
        if (_shutdownPrepared)
            return;

        _shutdownPrepared = true;
        _isShuttingDown = true;
        Close();
    }

    public void UpdateSnapshot(UsageSnapshot snapshot)
    {
        _viewModel.UpdateSnapshot(snapshot);
        UpdateAllValues();
        UpdateStatusDotColor(_viewModel.StatusPresentationState.DotColor);
    }

    public void UpdateSettings(TraySettings settings)
    {
        _isSynchronizingControls = true;
        try
        {
            _viewModel.UpdateSettings(settings);
            UpdateWindowButtonStates();
            UpdateCadenceComboSelection();
            UpdateAllValues();
        }
        finally
        {
            _isSynchronizingControls = false;
        }
    }

    public void SetIsRefreshing(bool isRefreshing)
    {
        _viewModel.IsRefreshing = isRefreshing;
        StatusText.Text = _viewModel.StatusText;
        UpdateStatusDotColor(_viewModel.StatusPresentationState.DotColor);
    }

    private void UpdateStatusDotColor(StatusDotColor dotColor)
    {
        StatusDot.Fill = dotColor switch
        {
            StatusDotColor.Gray => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 153, 153)),
            StatusDotColor.Blue => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0, 122, 255)),
            StatusDotColor.Green => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(52, 199, 89)),
            StatusDotColor.Orange => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 149, 0)),
            StatusDotColor.Red => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 59, 48)),
            _ => new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(153, 153, 153))
        };
    }

    private void UpdateAllValues()
    {
        StatusText.Text = _viewModel.StatusText;
        LastUpdateTimeText.Text = _viewModel.LastUpdateTimeText;
        TotalTpsValue.Text = _viewModel.TotalTpsText;
        RequestsValue.Text = _viewModel.RequestsPerMinuteText;
        InputValue.Text = _viewModel.InputTpsText;
        CachedValue.Text = _viewModel.CachedTpsText;
        OutputValue.Text = _viewModel.OutputTpsText;
        ReasoningValue.Text = _viewModel.ReasoningTpsText;
        ActiveSessionsValue.Text = _viewModel.ActiveSessionsText;
        CacheRatioValue.Text = _viewModel.CacheRatioText;

        InputLabel.Text = _viewModel.InputLabel;
        CachedLabel.Text = _viewModel.CachedLabel;
        OutputLabel.Text = _viewModel.OutputLabel;
        ReasoningLabel.Text = _viewModel.ReasoningLabel;
        ActiveSessionsLabel.Text = _viewModel.ActiveSessionsLabel;
        CacheRatioLabel.Text = _viewModel.CacheRatioLabel;
        RefreshCadenceLabel.Text = _viewModel.RefreshCadenceLabel;
        RequestsPerMinuteLabel.Text = _viewModel.RequestsPerMinuteLabel;
        TokenPerSecondLabel.Text = _viewModel.TokenPerSecondLabel;

        RefreshTooltip.Content = _viewModel.RefreshTooltip;
        OpenFolderTooltip.Content = _viewModel.OpenFolderTooltip;
    }

    private void UpdateWindowButtonStates()
    {
        MetricWindow selectedWindow = _viewModel.SelectedWindow;

        SetWindowButtonState(Window1MinButton, selectedWindow == MetricWindow.OneMinute);
        SetWindowButtonState(Window5MinButton, selectedWindow == MetricWindow.FiveMinutes);
        SetWindowButtonState(Window30MinButton, selectedWindow == MetricWindow.ThirtyMinutes);
        SetWindowButtonState(Window1HourButton, selectedWindow == MetricWindow.OneHour);

        Window1MinButton.Content = Localization.GetMetricWindowDisplayName(MetricWindow.OneMinute, _viewModel.Language);
        Window5MinButton.Content = Localization.GetMetricWindowDisplayName(MetricWindow.FiveMinutes, _viewModel.Language);
        Window30MinButton.Content = Localization.GetMetricWindowDisplayName(MetricWindow.ThirtyMinutes, _viewModel.Language);
        Window1HourButton.Content = Localization.GetMetricWindowDisplayName(MetricWindow.OneHour, _viewModel.Language);
    }

    private void SetWindowButtonState(System.Windows.Controls.Button button, bool isSelected)
    {
        if (isSelected)
        {
            button.SetResourceReference(System.Windows.Controls.Button.BackgroundProperty, "PanelAccentBrush");
            button.SetResourceReference(System.Windows.Controls.Button.ForegroundProperty, "PanelAccentForegroundBrush");
        }
        else
        {
            button.Background = System.Windows.Media.Brushes.Transparent;
            button.SetResourceReference(System.Windows.Controls.Button.ForegroundProperty, "PanelPrimaryTextBrush");
        }
    }

    private void UpdateCadenceComboSelection()
    {
        _isSynchronizingControls = true;
        try
        {
            RefreshCadence selectedCadence = _viewModel.SelectedCadence;

            foreach (var item in RefreshCadenceCombo.Items)
            {
                if (item is System.Windows.Controls.ComboBoxItem comboItem)
                {
                    if (Enum.TryParse<RefreshCadence>(comboItem.Tag?.ToString(), out RefreshCadence itemCadence))
                    {
                        comboItem.Content = Localization.GetRefreshCadenceCompactName(itemCadence, _viewModel.Language);
                        comboItem.IsSelected = itemCadence == selectedCadence;
                        if (itemCadence == selectedCadence)
                        {
                            RefreshCadenceCombo.SelectedItem = comboItem;
                        }
                    }
                }
            }
        }
        finally
        {
            _isSynchronizingControls = false;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshRequested?.Invoke();
    }

    private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFolderRequested?.Invoke();
    }

    private void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button)
        {
            if (Enum.TryParse<MetricWindow>(button.Tag?.ToString(), out MetricWindow window))
            {
                MetricWindowChanged?.Invoke(window);
            }
        }
    }

    private void RefreshCadenceCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isSynchronizingControls)
            return;

        if (RefreshCadenceCombo.SelectedItem is System.Windows.Controls.ComboBoxItem comboItem)
        {
            if (Enum.TryParse<RefreshCadence>(comboItem.Tag?.ToString(), out RefreshCadence cadence))
            {
                RefreshCadenceChanged?.Invoke(cadence);
            }
        }
    }
}
