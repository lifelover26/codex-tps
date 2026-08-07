using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace CodexTPSTray;

/// <summary>
/// Manages a monotonically increasing generation token for DPI reposition
/// callbacks. Consecutive DPI events merge: only the last queued callback
/// has a matching generation. Hiding or closing the window invalidates all
/// pending callbacks by incrementing the generation.
/// </summary>
internal sealed class DpiRepositionGuard
{
    private long _generation;

    public long Queue() => Interlocked.Increment(ref _generation);

    public bool IsCurrent(long generation) => Volatile.Read(ref _generation) == generation;

    public void Invalidate() => Interlocked.Increment(ref _generation);
}

public partial class OverlayWindow : Window
{
    private readonly IMonitorWorkAreaProvider _workAreaProvider;
    private readonly IWindowNativeInterop _native;
    private bool _isLocked;
    private bool _isShuttingDown;
    private bool _shutdownPrepared;
    private bool _isDragging;
    private EffectiveTheme _currentTheme = EffectiveTheme.Light;
    private OverlayOpacityPreference _currentOpacity = OverlayOpacityPreference.Default;
    private TraySettings? _currentSettings;
    private HwndSource? _hwndSource;
    private readonly DpiRepositionGuard _dpiGuard = new();
    private readonly DpiRepositionGuard _sizeNormalizationGuard = new();
    private IOverlayMenuCommandHandler? _menuCommandHandler;
    private IOverlayMenuStateProvider? _menuStateProvider;
    private readonly Dictionary<OverlayPositionPreset, WpfMenuItem> _positionMenuItems = new();
    private readonly Dictionary<OverlayOpacityPreference, WpfMenuItem> _opacityMenuItems = new();
    private readonly Dictionary<OverlayThemePreference, WpfMenuItem> _themeMenuItems = new();

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    private static readonly IntPtr HWND_TOPMOST = new(-1);

    internal interface IWindowNativeInterop
    {
        IntPtr GetHandle(Window window);
        bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
        int GetWindowLong(IntPtr hWnd, int nIndex);
        int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    }

    private sealed class DefaultWindowNativeInterop : IWindowNativeInterop
    {
        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        private static extern bool NativeSetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll", EntryPoint = "GetWindowRect")]
        private static extern bool NativeGetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong")]
        private static extern int NativeGetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong")]
        private static extern int NativeSetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        public IntPtr GetHandle(Window window)
        {
            return (PresentationSource.FromVisual(window) as HwndSource)?.Handle ?? IntPtr.Zero;
        }

        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags)
            => NativeSetWindowPos(hWnd, hWndInsertAfter, X, Y, cx, cy, uFlags);

        public bool GetWindowRect(IntPtr hWnd, out RECT lpRect)
            => NativeGetWindowRect(hWnd, out lpRect);

        public int GetWindowLong(IntPtr hWnd, int nIndex)
            => NativeGetWindowLong(hWnd, nIndex);

        public int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong)
            => NativeSetWindowLong(hWnd, nIndex, dwNewLong);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public event Action<double, double>? DragCompleted;

    public OverlayWindow(IMonitorWorkAreaProvider workAreaProvider)
        : this(workAreaProvider, new DefaultWindowNativeInterop())
    {
    }

    internal OverlayWindow(IMonitorWorkAreaProvider workAreaProvider, IWindowNativeInterop nativeInterop)
    {
        InitializeComponent();
        _workAreaProvider = workAreaProvider;
        _native = nativeInterop;

        OverlayThemeResources.Apply(Resources, EffectiveTheme.Dark);
        _currentTheme = EffectiveTheme.Dark;

        InitializeMenuItemMappings();

        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closing += OnClosing;
        IsVisibleChanged += OnIsVisibleChanged;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
        MouseRightButtonDown += OnMouseRightButtonDown;
        RootBorder.ContextMenuOpening += OnContextMenuOpening;
    }

    private void OnContextMenuOpening(object sender, System.Windows.Controls.ContextMenuEventArgs e)
    {
        if (_isLocked || _isShuttingDown)
        {
            e.Handled = true;
            OverlayContextMenu.IsOpen = false;
        }
    }

    private void InitializeMenuItemMappings()
    {
        _positionMenuItems[OverlayPositionPreset.TopLeft] = PositionTopLeft;
        _positionMenuItems[OverlayPositionPreset.TopRight] = PositionTopRight;
        _positionMenuItems[OverlayPositionPreset.MiddleLeft] = PositionMiddleLeft;
        _positionMenuItems[OverlayPositionPreset.MiddleRight] = PositionMiddleRight;
        _positionMenuItems[OverlayPositionPreset.BottomLeft] = PositionBottomLeft;
        _positionMenuItems[OverlayPositionPreset.BottomRight] = PositionBottomRight;

        _opacityMenuItems[OverlayOpacityPreference.Default] = OpacityDefault;
        _opacityMenuItems[OverlayOpacityPreference.Percent40] = Opacity40;
        _opacityMenuItems[OverlayOpacityPreference.Percent55] = Opacity55;
        _opacityMenuItems[OverlayOpacityPreference.Percent70] = Opacity70;
        _opacityMenuItems[OverlayOpacityPreference.Percent85] = Opacity85;
        _opacityMenuItems[OverlayOpacityPreference.Opaque] = OpacityOpaque;

        _themeMenuItems[OverlayThemePreference.FollowApplication] = ThemeFollowApp;
        _themeMenuItems[OverlayThemePreference.System] = ThemeSystem;
        _themeMenuItems[OverlayThemePreference.Light] = ThemeLight;
        _themeMenuItems[OverlayThemePreference.Dark] = ThemeDark;
    }

    internal void SetOverlayMenuCommandHandler(IOverlayMenuCommandHandler handler)
    {
        _menuCommandHandler = handler;
    }

    internal void SetOverlayMenuStateProvider(IOverlayMenuStateProvider provider)
    {
        _menuStateProvider = provider;
    }

    private void OnMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked || _isShuttingDown)
        {
            return;
        }

        UpdateContextMenuState();
    }

    internal void ApplyMenuTheme(EffectiveTheme theme)
    {
        // Menu resources are already applied by OverlayThemeResources in ApplyTheme()
    }

    internal void UpdateContextMenuState()
    {
        if (_menuStateProvider == null)
            return;

        var settings = _menuStateProvider.CurrentSettings;
        bool isVisible = _menuStateProvider.IsOverlayVisible;

        OverlayContextMenu.IsEnabled = !_isLocked && !_isShuttingDown;

        ShowOverlayMenuItem.IsChecked = settings.OverlayEnabled;
        LockOverlayMenuItem.IsChecked = settings.OverlayLocked;

        foreach (var preset in OverlayMenuDefinition.PositionPresets)
        {
            var item = _positionMenuItems[preset];
            item.IsChecked = preset == settings.OverlayPosition;
            item.IsEnabled = isVisible;
        }

        foreach (var opacity in OverlayMenuDefinition.OpacityPreferences)
        {
            _opacityMenuItems[opacity].IsChecked = opacity == settings.OverlayOpacity;
        }

        var overlayTheme = settings.OverlayTheme;
        foreach (var theme in OverlayMenuDefinition.ThemePreferences)
        {
            _themeMenuItems[theme].IsChecked = theme == overlayTheme;
        }

        UpdateMenuLocalization(settings.Language);
    }

    private void UpdateMenuLocalization(Language language)
    {
        ShowOverlayMenuItem.Header = Localization.ShowOverlayMenu(language);
        LockOverlayMenuItem.Header = Localization.LockOverlayMenu(language);
        PositionSubmenu.Header = Localization.PositionMenu(language);
        OpacitySubmenu.Header = Localization.BackgroundOpacityMenu(language);
        ThemeSubmenu.Header = Localization.OverlayThemeMenu(language);

        foreach (var preset in OverlayMenuDefinition.PositionPresets)
        {
            _positionMenuItems[preset].Header = OverlayMenuDefinition.GetPositionText(preset, language);
        }

        foreach (var opacity in OverlayMenuDefinition.OpacityPreferences)
        {
            _opacityMenuItems[opacity].Header = OverlayMenuDefinition.GetOpacityText(opacity, language);
        }

        foreach (var theme in OverlayMenuDefinition.ThemePreferences)
        {
            _themeMenuItems[theme].Header = OverlayMenuDefinition.GetThemeText(theme, language);
        }
    }

    private void OnShowOverlayClicked(object sender, RoutedEventArgs e)
    {
        if (_menuCommandHandler == null || _isLocked || _isShuttingDown)
            return;

        OverlayContextMenu.IsOpen = false;
        _menuCommandHandler.ToggleOverlay(ShowOverlayMenuItem.IsChecked);
    }

    private void OnLockOverlayClicked(object sender, RoutedEventArgs e)
    {
        if (_menuCommandHandler == null || _isShuttingDown)
            return;

        OverlayContextMenu.IsOpen = false;
        _menuCommandHandler.ToggleLock(LockOverlayMenuItem.IsChecked);
    }

    private void OnPositionClicked(object sender, RoutedEventArgs e)
    {
        if (_menuCommandHandler == null || _isLocked || _isShuttingDown || sender is not WpfMenuItem item)
            return;

        OverlayContextMenu.IsOpen = false;

        foreach (var kvp in _positionMenuItems)
        {
            if (kvp.Value == item)
            {
                _menuCommandHandler.SelectPosition(kvp.Key);
                return;
            }
        }
    }

    private void OnOpacityClicked(object sender, RoutedEventArgs e)
    {
        if (_menuCommandHandler == null || _isLocked || _isShuttingDown || sender is not WpfMenuItem item)
            return;

        OverlayContextMenu.IsOpen = false;

        foreach (var kvp in _opacityMenuItems)
        {
            if (kvp.Value == item)
            {
                _menuCommandHandler.SelectOpacity(kvp.Key);
                return;
            }
        }
    }

    private void OnThemeClicked(object sender, RoutedEventArgs e)
    {
        if (_menuCommandHandler == null || _isLocked || _isShuttingDown || sender is not WpfMenuItem item)
            return;

        OverlayContextMenu.IsOpen = false;

        foreach (var kvp in _themeMenuItems)
        {
            if (kvp.Value == item)
            {
                _menuCommandHandler.SelectTheme(kvp.Key);
                return;
            }
        }
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
        // Let WPF handle native DPI layout and re-rendering automatically (PerMonitorV2).
        // We do NOT set e.Handled or apply ScaleTransform here, which would cause double scaling.
        if (_isShuttingDown || _isDragging)
            return;

        long captured = _dpiGuard.Queue();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
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
        if (_isShuttingDown || !IsLoaded || !IsVisible || _isDragging)
            return;

        ExecuteDpiRepositionCore();
    }

    /// <summary>
    /// Core reposition logic without visibility/shutdown guards. Tests call this
    /// directly to verify preset re-anchoring and custom coordinate sync behavior.
    /// </summary>
    internal void ExecuteDpiRepositionCore()
    {
        NormalizeWindowSize(forceLayout: true);
        ApplyExtendedStyles();
        EnsureTopmost();

        if (_currentSettings == null)
            return;

        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero || !_native.GetWindowRect(hwnd, out RECT currentRect))
            return;

        if (_currentSettings.OverlayPosition.HasValue)
        {
            // Preset position: re-anchor to target monitor with new DPI margins
            int width = currentRect.Right - currentRect.Left;
            int height = currentRect.Bottom - currentRect.Top;
            if (width > 0 && height > 0)
            {
                var (left, top) = ResolvePosition(_currentSettings, width, height);
                _native.SetWindowPos(hwnd, IntPtr.Zero, (int)left, (int)top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                EnsureTopmost();
            }
        }
        else
        {
            // Custom position: sync final WPF-placed coordinates back to settings after DPI change
            // to avoid restoring stale physical coordinates on next launch.
            // Do NOT jump to old OverlayLeft/OverlayTop — let WPF's native DPI placement stand.
            SyncCustomPositionFromWindowRect(currentRect);
        }
    }

    private void SyncCustomPositionFromWindowRect(RECT physicalRect)
    {
        // Position storage uses physical screen coordinates (consistent with GetWindowRect,
        // SetWindowPos, and Screen.WorkingArea from WinForms). Save the final physical
        // position as-placed by WPF after DPI change so next launch resumes from the correct spot.
        try
        {
            int physicalWidth = physicalRect.Right - physicalRect.Left;
            int physicalHeight = physicalRect.Bottom - physicalRect.Top;
            if (physicalWidth <= 0 || physicalHeight <= 0)
                return;

            DragCompleted?.Invoke(physicalRect.Left, physicalRect.Top);
        }
        catch
        {
        }
    }

    internal void ApplyTheme(EffectiveTheme theme)
    {
        if (_currentTheme == theme)
            return;

        _currentTheme = theme;
        OverlayThemeResources.Apply(Resources, theme);
        ApplyOpacityToBorder();
        QueueWindowSizeNormalization(forceLayout: false);
    }

    public void ApplyOpacity(OverlayOpacityPreference preference)
    {
        _currentOpacity = preference;
        ApplyOpacityToBorder();
        QueueWindowSizeNormalization(forceLayout: false);
    }

    private void ApplyOpacityToBorder()
    {
        if (_currentOpacity == OverlayOpacityPreference.Default)
        {
            RootBorder.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, "OverlayBackgroundBrush");
        }
        else
        {
            System.Windows.Media.Color color = OverlayOpacityCalculator.CreateBackgroundColor(_currentOpacity, _currentTheme);
            RootBorder.Background = new SolidColorBrush(color);
        }
    }

    public (double Left, double Top, double Width, double Height)? GetCurrentRect()
    {
        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero)
            return null;

        if (!_native.GetWindowRect(hwnd, out RECT rect))
            return null;

        return (rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
    }

    public void MoveToPosition(double left, double top)
    {
        NormalizeWindowSize(forceLayout: false);

        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero)
            return;

        _native.SetWindowPos(hwnd, IntPtr.Zero, (int)left, (int)top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        EnsureTopmost();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NormalizeWindowSize(forceLayout: true);
        ApplyExtendedStyles();
        EnsureTopmost();
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        // When window becomes hidden, invalidate any pending DPI reposition so a
        // queued callback from before the hide cannot reposition the window after
        // a subsequent Show.
        if (!(bool)e.NewValue)
        {
            _dpiGuard.Invalidate();
            _sizeNormalizationGuard.Invalidate();
            return;
        }

        QueueWindowSizeNormalization(forceLayout: true);
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
        _sizeNormalizationGuard.Invalidate();
        if (_hwndSource != null)
        {
            _hwndSource.DpiChanged -= OnHwndSourceDpiChanged;
            _hwndSource = null;
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked)
            return;

        _isDragging = true;
        try
        {
            DragMove();
        }
        finally
        {
            _isDragging = false;
        }

        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd != IntPtr.Zero && _native.GetWindowRect(hwnd, out RECT rect))
        {
            // Save physical screen coordinates (consistent with GetWindowRect/SetWindowPos/Screen.WorkingArea).
            DragCompleted?.Invoke(rect.Left, rect.Top);
        }
    }

    private void ApplyExtendedStyles()
    {
        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero)
            return;

        int currentStyle = _native.GetWindowLong(hwnd, -20);

        int originalStyle = currentStyle;

        currentStyle |= WS_EX_TOOLWINDOW;
        currentStyle |= WS_EX_NOACTIVATE;

        if (_isLocked)
        {
            currentStyle |= WS_EX_TRANSPARENT;
        }
        else
        {
            currentStyle &= ~WS_EX_TRANSPARENT;
        }

        if (currentStyle != originalStyle)
        {
            _native.SetWindowLong(hwnd, -20, currentStyle);
            _native.SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
    }

    private void EnsureTopmost()
    {
        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero)
            return;

        Topmost = true;
        _native.SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_FRAMECHANGED);
    }

    internal void ReassertTopmost()
    {
        NormalizeWindowSize(forceLayout: true);
        EnsureTopmost();
    }

    public void UpdateContent(string[] lines)
    {
        if (_isShuttingDown)
            return;

        if (lines.Length >= 6)
        {
            TitleText.Text = lines[0];
            WindowText.Text = lines[1];
            TpsText.Text = lines[2];
            RequestsText.Text = lines[3];
            SessionsText.Text = lines[4];
            CacheText.Text = lines[5];
        }

        QueueWindowSizeNormalization(forceLayout: false);
    }

    public void UpdateSettings(TraySettings settings)
    {
        if (_isShuttingDown)
            return;

        _currentSettings = settings;

        bool wasLocked = _isLocked;
        _isLocked = settings.OverlayLocked;

        if (wasLocked != _isLocked)
        {
            ApplyExtendedStyles();
            EnsureTopmost();

            OverlayContextMenu.IsEnabled = !_isLocked;
            if (_isLocked)
            {
                OverlayContextMenu.IsOpen = false;
            }
        }

        _currentOpacity = settings.OverlayOpacity;
        ApplyOpacityToBorder();
        QueueWindowSizeNormalization(forceLayout: true);
    }

    public void ResetPosition(TraySettings settings)
    {
        NormalizeWindowSize(forceLayout: false);

        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero)
            return;

        if (!_native.GetWindowRect(hwnd, out RECT rect))
            return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
            return;

        var (left, top) = ResolvePosition(settings, width, height);
        _native.SetWindowPos(hwnd, IntPtr.Zero, (int)left, (int)top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        EnsureTopmost();
    }

    public (double Left, double Top) ResolvePosition(TraySettings settings, double width, double height)
    {
        System.Windows.Size overlaySize = new System.Windows.Size(width, height);
        Thickness dipMargin = new Thickness(16);

        if (settings.OverlayPosition.HasValue)
        {
            var allMonitorInfos = _workAreaProvider.GetAllMonitorInfos();
            var primaryWorkArea = _workAreaProvider.GetPrimaryWorkArea();

            MonitorInfo primaryMonitorInfo = new MonitorInfo(
                DeviceName: string.Empty,
                WorkingArea: primaryWorkArea,
                IsPrimary: true
            );

            foreach (var info in allMonitorInfos)
            {
                if (info.IsPrimary)
                {
                    primaryMonitorInfo = info;
                    break;
                }
            }

            MonitorInfo targetMonitor = OverlayPositionCalculator.ResolveTargetMonitor(
                settings.OverlayMonitorDeviceName,
                allMonitorInfos,
                primaryMonitorInfo);

            Thickness physicalMargin = DpiHelper.ConvertDipMarginToPhysical(dipMargin, targetMonitor.DpiX, targetMonitor.DpiY);

            return OverlayPositionCalculator.CalculatePresetPosition(
                settings.OverlayPosition.Value,
                overlaySize,
                targetMonitor.WorkingArea,
                physicalMargin
            );
        }

        var allMonitorInfosForCustom = _workAreaProvider.GetAllMonitorInfos();
        MonitorInfo primaryMonitor = allMonitorInfosForCustom.FirstOrDefault(i => i.IsPrimary)
            ?? new MonitorInfo(string.Empty, _workAreaProvider.GetPrimaryWorkArea(), true);

        Thickness primaryPhysicalMargin = DpiHelper.ConvertDipMarginToPhysical(dipMargin, primaryMonitor.DpiX, primaryMonitor.DpiY);
        var allWorkAreas = _workAreaProvider.GetAllWorkAreas();

        return OverlayPositionCalculator.CalculatePosition(
            settings.OverlayLeft,
            settings.OverlayTop,
            overlaySize,
            primaryMonitor.WorkingArea,
            allWorkAreas,
            primaryPhysicalMargin
        );
    }

    public void PrepareForShutdown()
    {
        if (_shutdownPrepared)
            return;

        _shutdownPrepared = true;
        _isShuttingDown = true;
        Close();
    }

    private void QueueWindowSizeNormalization(bool forceLayout)
    {
        if (_isShuttingDown || _isDragging || !IsLoaded || !IsVisible)
            return;

        long captured = _sizeNormalizationGuard.Queue();
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            if (!_sizeNormalizationGuard.IsCurrent(captured))
                return;

            NormalizeWindowSize(forceLayout);
        }));
    }

    private void NormalizeWindowSize(bool forceLayout)
    {
        if (_isShuttingDown || _isDragging || !IsInitialized || !IsLoaded)
            return;

        // Keep the logical width deterministic. SizeToContent remains height-only,
        // so text updates cannot turn a DPI transition into a wider window.
        bool logicalSizeChanged = Width != OverlayWindowSizeCalculator.WidthDip
            || MinWidth != OverlayWindowSizeCalculator.WidthDip
            || MaxWidth != OverlayWindowSizeCalculator.WidthDip
            || SizeToContent != SizeToContent.Height;

        Width = OverlayWindowSizeCalculator.WidthDip;
        MinWidth = OverlayWindowSizeCalculator.WidthDip;
        MaxWidth = OverlayWindowSizeCalculator.WidthDip;
        SizeToContent = SizeToContent.Height;
        if (forceLayout || logicalSizeChanged || ActualHeight <= 0)
        {
            InvalidateMeasure();
            UpdateLayout();
        }

        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero || !_native.GetWindowRect(hwnd, out RECT currentRect))
            return;

        double dpi = DpiHelper.GetDpiForWindow(hwnd);
        var expected = OverlayWindowSizeCalculator.CalculatePhysicalSize(ActualHeight, dpi, dpi);
        if (expected.Width <= 0 || expected.Height <= 0)
            return;

        int actualWidth = currentRect.Right - currentRect.Left;
        int actualHeight = currentRect.Bottom - currentRect.Top;
        if (!OverlayWindowSizeCalculator.Differs(actualWidth, expected.Width)
            && !OverlayWindowSizeCalculator.Differs(actualHeight, expected.Height))
        {
            return;
        }

        _native.SetWindowPos(
            hwnd,
            IntPtr.Zero,
            currentRect.Left,
            currentRect.Top,
            expected.Width,
            expected.Height,
            SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
