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
    private bool _isOverlayEnabled;
    private bool _isShuttingDown;
    private bool _shutdownPrepared;
    private bool _isDragging;
    // Pure origin-absolute projection drag state. The window position is always
    // computed as an absolute projection from the immutable drag origin — never
    // accumulated frame-by-frame, never re-anchored on axis or Shift changes.
    //
    // 1. dragOriginMouse / dragOriginWindowLeft/Top: the mouse and window
    //    position recorded at mouse-down. This is the origin of the ENTIRE
    //    drag gesture and is IMMUTABLE from mouse-down until EndDrag/CancelDrag.
    //    Shift presses, releases, or repeated toggles never recompute it.
    //    All displacement (dx/dy) is relative to this single origin.
    //
    // 2. currentAxis: None, Horizontal, or Vertical. A Shift state transition
    //    resets this to None so the next frame re-enters the calculator from a
    //    neutral state, but the origin is untouched. The calculator produces
    //    the direction and matching coordinates together. A short fixed-pixel
    //    blend is used only while entering or changing rails.
    //
    // Because the origin never changes during a gesture, the cursor returning
    // to it always produces the original window position — no drift, ever.
    private System.Drawing.Point _dragOriginMouse;
    private int _dragOriginWindowLeft;
    private int _dragOriginWindowTop;
    private AxisLockDirection _currentAxis;
    private bool _previousShiftHeld;
    private EffectiveTheme _currentTheme = EffectiveTheme.Light;
    private OverlayOpacityPreference _currentOpacity = OverlayOpacityPreference.Default;
    private TraySettings? _currentSettings;
    private HwndSource? _hwndSource;
    private bool _hwndHookAttached;
    private readonly TopmostHealthMonitor _topmostHealthMonitor;
    private readonly DpiRepositionGuard _dpiGuard = new();
    private readonly DpiRepositionGuard _sizeNormalizationGuard = new();
    private IOverlayMenuCommandHandler? _menuCommandHandler;
    private IOverlayMenuStateProvider? _menuStateProvider;
    private readonly Dictionary<OverlayPositionPreset, WpfMenuItem> _positionMenuItems = new();
    private readonly Dictionary<OverlayOpacityPreference, WpfMenuItem> _opacityMenuItems = new();
    private readonly Dictionary<OverlayThemePreference, WpfMenuItem> _themeMenuItems = new();
    private readonly Dictionary<OverlayPositionMemoryMode, WpfMenuItem> _positionMemoryModeMenuItems = new();
    private readonly Dictionary<OverlayAppearanceMemoryMode, WpfMenuItem> _appearanceMemoryModeMenuItems = new();

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int GWL_EXSTYLE = -20;
    private const int WM_WINDOWPOSCHANGED = 0x0047;

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

    /// <summary>
    /// Raised when ResolvePosition produces updated custom-position settings
    /// (legacy migration or a first-seen display inheriting its own record).
    /// The owner must persist the new settings and push them back via
    /// UpdateSettings. Never raised for a user drag — drags go through
    /// DragCompleted. Never raised when the restored state is unchanged.
    /// </summary>
    public event Action<TraySettings>? CustomPositionSettingsUpdated;

    internal bool IsDragging => _isDragging;
    internal bool IsAxisLocked => _isDragging && _currentAxis != AxisLockDirection.None;
    internal AxisLockDirection CurrentAxisLockDirection => _currentAxis;

    public OverlayWindow(IMonitorWorkAreaProvider workAreaProvider)
        : this(workAreaProvider, new DefaultWindowNativeInterop())
    {
    }

    internal OverlayWindow(IMonitorWorkAreaProvider workAreaProvider, IWindowNativeInterop nativeInterop)
        : this(workAreaProvider, nativeInterop, null, null)
    {
    }

    internal OverlayWindow(IMonitorWorkAreaProvider workAreaProvider, IWindowNativeInterop nativeInterop, ITopmostHealthTimer? healthTimer, IDispatcher? healthDispatcher)
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
        MouseMove += OnMouseMove;
        MouseLeftButtonUp += OnMouseLeftButtonUp;
        LostMouseCapture += OnLostMouseCapture;
        MouseRightButtonDown += OnMouseRightButtonDown;
        RootBorder.ContextMenuOpening += OnContextMenuOpening;

        _topmostHealthMonitor = new TopmostHealthMonitor(
            new OverlayTopmostHealthTarget(this),
            healthDispatcher ?? new WpfDispatcher(),
            healthTimer ?? new DispatcherTopmostHealthTimer(),
            TimeSpan.FromSeconds(2));
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

        _positionMemoryModeMenuItems[OverlayPositionMemoryMode.SharedAcrossDisplays] = PositionMemoryShared;
        _positionMemoryModeMenuItems[OverlayPositionMemoryMode.RememberPerDisplay] = PositionMemoryRememberPerDisplay;

        _appearanceMemoryModeMenuItems[OverlayAppearanceMemoryMode.SharedAcrossDisplays] = AppearanceMemoryShared;
        _appearanceMemoryModeMenuItems[OverlayAppearanceMemoryMode.RememberPerDisplay] = AppearanceMemoryRememberPerDisplay;
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

        OverlayPositionPreset? activePreset = ResolveActivePreset(settings);
        foreach (var preset in OverlayMenuDefinition.PositionPresets)
        {
            var item = _positionMenuItems[preset];
            item.IsChecked = preset == activePreset;
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

        foreach (var mode in OverlayMenuDefinition.PositionMemoryModes)
        {
            _positionMemoryModeMenuItems[mode].IsChecked = mode == settings.PositionMemoryMode;
        }

        foreach (var mode in OverlayMenuDefinition.AppearanceMemoryModes)
        {
            _appearanceMemoryModeMenuItems[mode].IsChecked = mode == settings.AppearanceMemoryMode;
        }

        UpdateMenuLocalization(settings.Language);
    }

    private OverlayPositionPreset? ResolveActivePreset(TraySettings settings)
    {
        var (allMonitors, primary) = GetMonitorsAndPrimary();
        IntPtr hwnd = _native.GetHandle(this);
        double currentLeft = 0.0;
        double currentTop = 0.0;
        double width = 200.0;
        double height = 120.0;
        if (hwnd != IntPtr.Zero && _native.GetWindowRect(hwnd, out RECT rect))
        {
            currentLeft = rect.Left;
            currentTop = rect.Top;
            width = rect.Right - rect.Left;
            height = rect.Bottom - rect.Top;
        }

        if (width <= 0 || height <= 0)
        {
            width = 200.0;
            height = 120.0;
        }

        MonitorInfo currentMonitor = OverlayPositionCalculator.FindBestMonitor(
            currentLeft, currentTop, width, height, allMonitors, primary);
        return OverlayPositionCoordinator.GetActivePreset(settings, currentMonitor);
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

    private void UpdateMenuLocalization(Language language)
    {
        ShowOverlayMenuItem.Header = Localization.ShowOverlayMenu(language);
        LockOverlayMenuItem.Header = Localization.LockOverlayMenu(language);
        PositionSubmenu.Header = Localization.PositionMenu(language);
        MemorySubmenu.Header = Localization.MemoryMenu(language);
        AppearanceMemorySubmenu.Header = Localization.AppearanceMemoryMenu(language);
        PositionMemorySubmenu.Header = Localization.PositionMenu(language);
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

        foreach (var mode in OverlayMenuDefinition.PositionMemoryModes)
        {
            _positionMemoryModeMenuItems[mode].Header = OverlayMenuDefinition.GetPositionMemoryModeText(mode, language);
        }

        foreach (var mode in OverlayMenuDefinition.AppearanceMemoryModes)
        {
            _appearanceMemoryModeMenuItems[mode].Header = OverlayMenuDefinition.GetAppearanceMemoryModeText(mode, language);
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

    private void OnPositionMemoryModeClicked(object sender, RoutedEventArgs e)
    {
        if (_menuCommandHandler == null || _isLocked || _isShuttingDown || sender is not WpfMenuItem item)
            return;

        OverlayContextMenu.IsOpen = false;

        foreach (var kvp in _positionMemoryModeMenuItems)
        {
            if (kvp.Value == item)
            {
                _menuCommandHandler.SelectPositionMemoryMode(kvp.Key);
                return;
            }
        }
    }

    private void OnAppearanceMemoryModeClicked(object sender, RoutedEventArgs e)
    {
        if (_menuCommandHandler == null || _isLocked || _isShuttingDown || sender is not WpfMenuItem item)
            return;

        OverlayContextMenu.IsOpen = false;

        foreach (var kvp in _appearanceMemoryModeMenuItems)
        {
            if (kvp.Value == item)
            {
                _menuCommandHandler.SelectAppearanceMemoryMode(kvp.Key);
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
        AttachHwndHook();
    }

    private void AttachHwndHook()
    {
        if (_hwndHookAttached)
            return;
        if (_hwndSource == null)
            return;
        _hwndSource.AddHook(OnHwndSourceHook);
        _hwndHookAttached = true;
    }

    private void DetachHwndHook()
    {
        if (!_hwndHookAttached)
            return;
        _hwndSource?.RemoveHook(OnHwndSourceHook);
        _hwndHookAttached = false;
    }

    private IntPtr OnHwndSourceHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_WINDOWPOSCHANGED)
        {
            _topmostHealthMonitor.QueueCheck();
        }
        handled = false;
        return IntPtr.Zero;
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

        int width = currentRect.Right - currentRect.Left;
        int height = currentRect.Bottom - currentRect.Top;
        if (width <= 0 || height <= 0)
            return;

        // Both preset and custom positions re-anchor from saved state against the
        // current monitor's work area. For custom mode this is idempotent —
        // repeated DPI or display-config notifications re-derive the same position
        // and never accumulate drift. DragCompleted is NOT fired in either branch:
        // auto-reposition must never masquerade as a user drag or write
        // intermediate physical coordinates back to settings. Legacy migration and
        // first-seen display inheritance are surfaced via CustomPositionSettingsUpdated.
        var (left, top) = ResolvePosition(_currentSettings, width, height);
        _native.SetWindowPos(hwnd, IntPtr.Zero, (int)left, (int)top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        EnsureTopmost();
    }

    /// <summary>
    /// Applies theme and opacity as a single coordinated state. Theme resources
    /// are rebuilt only when the effective theme actually changes; opacity is
    /// re-applied once against the final theme so there is no intermediate
    /// flicker. Returns immediately when neither value changed, avoiding
    /// redundant resource rebuilds or border re-application.
    /// </summary>
    internal void ApplyAppearance(EffectiveTheme theme, OverlayOpacityPreference opacity)
    {
        bool themeChanged = _currentTheme != theme;
        bool opacityChanged = _currentOpacity != opacity;
        if (!themeChanged && !opacityChanged)
            return;

        if (themeChanged)
        {
            _currentTheme = theme;
            OverlayThemeResources.Apply(Resources, theme);
        }

        _currentOpacity = opacity;
        ApplyOpacityToBorder();
        QueueWindowSizeNormalization(forceLayout: false);
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
        if (_currentOpacity == preference)
            return;

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
            CancelDrag();
            _dpiGuard.Invalidate();
            _sizeNormalizationGuard.Invalidate();
        }
        else
        {
            QueueWindowSizeNormalization(forceLayout: true);
        }

        UpdateTopmostHealthMonitorEnabled();
    }

    private void UpdateTopmostHealthMonitorEnabled()
    {
        if (!_isShuttingDown && IsVisible && _isOverlayEnabled)
            _topmostHealthMonitor.Start();
        else
            _topmostHealthMonitor.Stop();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        CancelDrag();
        _dpiGuard.Invalidate();
        _sizeNormalizationGuard.Invalidate();
        _topmostHealthMonitor.PrepareForShutdown();
        DetachHwndHook();
        if (_hwndSource != null)
        {
            _hwndSource.DpiChanged -= OnHwndSourceDpiChanged;
            _hwndSource = null;
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        HandleMouseLeftButtonDown(
            GetCursorScreenPosition(),
            Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    /// <summary>
    /// Begins a unified manual drag. Both ordinary free drag and Shift axis
    /// drag go through this path so Shift can be detected mid-drag via
    /// <see cref="HandleDragMove"/>. The drag always uses mouse capture +
    /// SetWindowPos in physical screen coordinates — never DragMove or
    /// Window.Left/Top — to keep PerMonitorV2 DPI semantics consistent.
    /// </summary>
    internal void HandleMouseLeftButtonDown(System.Drawing.Point mouseScreen, bool shiftHeld)
    {
        if (_isLocked || _isShuttingDown || _isDragging)
            return;

        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero || !_native.GetWindowRect(hwnd, out RECT rect))
            return;

        _isDragging = true;
        _previousShiftHeld = shiftHeld;
        _dragOriginMouse = mouseScreen;
        _dragOriginWindowLeft = rect.Left;
        _dragOriginWindowTop = rect.Top;
        _currentAxis = AxisLockDirection.None;

        try
        {
            CaptureMouse();
        }
        catch
        {
            // Capture can fail in headless/test contexts; SetWindowPos still works.
        }
    }

    /// <summary>
    /// Unified move handler using pure origin-absolute projection. All target
    /// coordinates are computed as absolute projections from the immutable drag
    /// origin — never accumulated frame-by-frame, never re-anchored on axis or
    /// Shift state changes. A Shift press/release only resets the axis state to
    /// None and then continues processing the same frame; the origin stays at
    /// mouse-down for the whole gesture. While Shift is held, a strong direction
    /// is projected onto a hard horizontal or vertical rail. Crossing the
    /// opposite-axis margin uses one short fixed-pixel blend between the two
    /// origin-based rails. Because the calculator returns state and coordinates
    /// together and the origin never changes, returning to it cannot accumulate
    /// drift.
    /// </summary>
    internal void HandleDragMove(System.Drawing.Point mouseScreen, bool shiftHeld)
    {
        if (!_isDragging)
            return;

        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero)
            return;

        // Detect Shift state transitions. Reset the axis state so the next
        // projection re-enters from a neutral direction, but DO NOT re-anchor
        // the drag origin — it stays at the mouse-down position for the whole
        // gesture. Fall through and process this frame with the new state.
        if (shiftHeld != _previousShiftHeld)
        {
            _previousShiftHeld = shiftHeld;
            _currentAxis = AxisLockDirection.None;
        }

        // Absolute displacement from the immutable drag origin.
        int dx = mouseScreen.X - _dragOriginMouse.X;
        int dy = mouseScreen.Y - _dragOriginMouse.Y;

        if (!shiftHeld)
        {
            // Free drag: absolute projection from drag origin onto both axes.
            MoveWindowTo(hwnd, _dragOriginWindowLeft + dx, _dragOriginWindowTop + dy);
            return;
        }

        // Shift held — state and coordinates come from one origin-absolute
        // projection. The short transition band is part of this projection;
        // there is no second diagnostic-only direction path.
        AxisProjection projection = AxisLockCalculator.Project(
            dx,
            dy,
            _dragOriginWindowLeft,
            _dragOriginWindowTop,
            _currentAxis);
        _currentAxis = projection.Direction;

        MoveWindowTo(hwnd, projection.Left, projection.Top);
    }

    /// <summary>
    /// Moves the window to the target position and reads back the actual rect.
    /// The readback accounts for DPI/multi-monitor rounding but is NOT used as
    /// an accumulation base — the drag origin remains the projection base.
    /// </summary>
    private void MoveWindowTo(IntPtr hwnd, int targetLeft, int targetTop)
    {
        _native.SetWindowPos(hwnd, IntPtr.Zero, targetLeft, targetTop, 0, 0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);

        // Read back to account for DPI/multi-monitor rounding, but the drag
        // origin remains the projection base — no accumulation.
        _native.GetWindowRect(hwnd, out RECT _);
    }

    /// <summary>
    /// Completes the drag: releases capture and fires DragCompleted exactly
    /// once with the final physical coordinates. Idempotent so a second call
    /// (e.g. MouseLeftButtonUp after LostMouseCapture) cannot double-fire.
    /// </summary>
    internal void EndDrag()
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        _currentAxis = AxisLockDirection.None;
        _previousShiftHeld = false;

        ReleaseMouseCaptureSafely();

        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd != IntPtr.Zero && _native.GetWindowRect(hwnd, out RECT rect))
        {
            // Save physical screen coordinates (consistent with GetWindowRect/SetWindowPos/Screen.WorkingArea).
            DragCompleted?.Invoke(rect.Left, rect.Top);
        }
    }

    /// <summary>
    /// Aborts the drag without firing DragCompleted, used when mouse capture
    /// is lost or the window hides/closes mid-drag. Idempotent.
    /// </summary>
    internal void CancelDrag()
    {
        if (!_isDragging)
            return;

        _isDragging = false;
        _currentAxis = AxisLockDirection.None;
        _previousShiftHeld = false;

        ReleaseMouseCaptureSafely();
    }

    private void OnMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (!_isDragging)
            return;
        HandleDragMove(GetCursorScreenPosition(), Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
    }

    private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging)
            return;
        EndDrag();
    }

    private void OnLostMouseCapture(object sender, System.Windows.Input.MouseEventArgs e)
    {
        CancelDrag();
    }

    private void ReleaseMouseCaptureSafely()
    {
        try
        {
            if (IsMouseCaptured)
                ReleaseMouseCapture();
        }
        catch
        {
        }
    }

    private System.Drawing.Point GetCursorScreenPosition()
    {
        try
        {
            return System.Windows.Forms.Cursor.Position;
        }
        catch
        {
            return new System.Drawing.Point(0, 0);
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

    /// <summary>
    /// Reads the native WS_EX_TOPMOST bit via GetWindowLong(GWL_EXSTYLE). This is
    /// the authoritative topmost state source — the WPF Topmost property may still
    /// be true while the native Z-order state has drifted. Returns true when no
    /// HWND is available yet so the monitor does not spuriously recover.
    /// </summary>
    internal bool IsNativeTopmostSet()
    {
        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd == IntPtr.Zero)
            return true;
        int exStyle = _native.GetWindowLong(hwnd, GWL_EXSTYLE);
        return (exStyle & WS_EX_TOPMOST) != 0;
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

    /// <summary>
    /// Re-anchors the overlay window position after a display configuration
    /// change (resolution, DPI, topology, monitor connect/disconnect). Delegates
    /// to <see cref="ExecuteDpiReposition"/> which already guards against
    /// shutdown, !IsLoaded, !IsVisible, and _isDragging. The re-anchor is
    /// idempotent — it re-derives position from saved normalized ratios and
    /// never fires DragCompleted or writes intermediate coordinates back to
    /// settings.
    /// </summary>
    internal void ReconcilePlacement()
    {
        ExecuteDpiReposition();
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
        _isOverlayEnabled = settings.OverlayEnabled;

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
        UpdateTopmostHealthMonitorEnabled();
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
        var (allMonitors, primaryMonitor) = GetMonitorsAndPrimary();

        double currentLeft = 0.0;
        double currentTop = 0.0;
        IntPtr hwnd = _native.GetHandle(this);
        if (hwnd != IntPtr.Zero && _native.GetWindowRect(hwnd, out RECT currentRect))
        {
            currentLeft = currentRect.Left;
            currentTop = currentRect.Top;
        }

        Thickness dipMargin = new Thickness(16);

        var (resolvedLeft, resolvedTop, updatedSettings) = OverlayPositionCoordinator.ResolveRestorePosition(
            settings,
            currentLeft,
            currentTop,
            width,
            height,
            allMonitors,
            primaryMonitor,
            dipMargin);

        if (updatedSettings != null)
        {
            CustomPositionSettingsUpdated?.Invoke(updatedSettings);
        }

        return (resolvedLeft, resolvedTop);
    }

    public void PrepareForShutdown()
    {
        if (_shutdownPrepared)
            return;

        _shutdownPrepared = true;
        _isShuttingDown = true;
        _topmostHealthMonitor.PrepareForShutdown();
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

    internal TopmostHealthMonitor TopmostHealthMonitorForTest => _topmostHealthMonitor;
    internal bool IsHwndHookAttachedForTest => _hwndHookAttached;
    internal bool IsOverlayEnabledForTest => _isOverlayEnabled;

    internal void InvokeHwndHookForTest(int msg)
    {
        bool handled = false;
        OnHwndSourceHook(_native.GetHandle(this), msg, IntPtr.Zero, IntPtr.Zero, ref handled);
    }

    private sealed class OverlayTopmostHealthTarget : ITopmostHealthTarget
    {
        private readonly OverlayWindow _owner;

        public OverlayTopmostHealthTarget(OverlayWindow owner) => _owner = owner;

        public bool IsVisible => _owner.IsVisible;
        public bool IsEnabled => _owner._isOverlayEnabled && !_owner._isShuttingDown;
        public bool IsNativeTopmostSet() => _owner.IsNativeTopmostSet();
        public void RecoverTopmost() => _owner.EnsureTopmost();
    }
}
