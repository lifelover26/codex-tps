using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;

namespace CodexTPSTray;

public partial class OverlayWindow : Window
{
    private readonly IMonitorWorkAreaProvider _workAreaProvider;
    private bool _isLocked;
    private bool _isShuttingDown;
    private bool _shutdownPrepared;

    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_FRAMECHANGED = 0x0020;

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    public event Action<double, double>? DragCompleted;

    public OverlayWindow(IMonitorWorkAreaProvider workAreaProvider)
    {
        InitializeComponent();
        _workAreaProvider = workAreaProvider;

        Loaded += OnLoaded;
        Closing += OnClosing;
        MouseLeftButtonDown += OnMouseLeftButtonDown;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ApplyExtendedStyles();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (!_isShuttingDown)
        {
            e.Cancel = true;
            Hide();
        }
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_isLocked)
            return;

        DragMove();

        var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource != null && GetWindowRect(hwndSource.Handle, out RECT rect))
        {
            DragCompleted?.Invoke(rect.Left, rect.Top);
        }
    }

    private void ApplyExtendedStyles()
    {
        var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource == null)
            return;

        IntPtr hwnd = hwndSource.Handle;
        int currentStyle = GetWindowLong(hwnd, -20);

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
            SetWindowLong(hwnd, -20, currentStyle);
            SetWindowPos(hwnd, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
        }
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
    }

    public void UpdateSettings(TraySettings settings)
    {
        if (_isShuttingDown)
            return;

        bool wasLocked = _isLocked;
        _isLocked = settings.OverlayLocked;

        if (wasLocked != _isLocked)
        {
            ApplyExtendedStyles();
        }
    }

    public void ResetPosition(double? savedLeft = null, double? savedTop = null)
    {
        var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
        if (hwndSource == null)
            return;

        IntPtr hwnd = hwndSource.Handle;

        if (!GetWindowRect(hwnd, out RECT rect))
            return;

        int width = rect.Right - rect.Left;
        int height = rect.Bottom - rect.Top;

        if (width <= 0 || height <= 0)
            return;

        System.Windows.Size overlaySize = new System.Windows.Size(width, height);
        Rect primaryWorkArea = _workAreaProvider.GetPrimaryWorkArea();
        var allWorkAreas = _workAreaProvider.GetAllWorkAreas();
        Thickness margin = new Thickness(16);

        var (left, top) = OverlayPositionCalculator.CalculatePosition(
            savedLeft,
            savedTop,
            overlaySize,
            primaryWorkArea,
            allWorkAreas,
            margin
        );

        SetWindowPos(hwnd, IntPtr.Zero, (int)left, (int)top, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public void PrepareForShutdown()
    {
        if (_shutdownPrepared)
            return;

        _shutdownPrepared = true;
        _isShuttingDown = true;
        Close();
    }
}