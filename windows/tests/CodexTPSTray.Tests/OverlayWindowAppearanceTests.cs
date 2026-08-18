using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace CodexTPSTray.Tests;

public class OverlayWindowAppearanceTests
{
    private static readonly IntPtr FakeHwnd = new(0x1234);

    private sealed class FakeWindowNativeInterop : OverlayWindow.IWindowNativeInterop
    {
        public IntPtr GetHandle(Window window) => FakeHwnd;
        public bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags) => true;
        public bool GetWindowRect(IntPtr hWnd, out OverlayWindow.RECT lpRect)
        {
            lpRect = new OverlayWindow.RECT { Left = 100, Top = 100, Right = 300, Bottom = 200 };
            return true;
        }
        public int GetWindowLong(IntPtr hWnd, int nIndex) => 0;
        public int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong) => 0;
    }

    private sealed class FakeWorkAreaProvider : IMonitorWorkAreaProvider
    {
        public Rect GetPrimaryWorkArea() => new(0, 0, 1920, 1040);
        public IReadOnlyList<Rect> GetAllWorkAreas() => new[] { new Rect(0, 0, 1920, 1040) };
        public IReadOnlyList<MonitorInfo> GetAllMonitorInfos() => new[]
        {
            new MonitorInfo(DeviceName: @"\\.\DISPLAY1", WorkingArea: new Rect(0, 0, 1920, 1040), IsPrimary: true)
        };
    }

    private static OverlayWindow CreateWindow()
        => new(new FakeWorkAreaProvider(), new FakeWindowNativeInterop());

    private static (byte A, byte R, byte G, byte B) GetBorderArgb(OverlayWindow window)
    {
        var border = (System.Windows.Controls.Border)window.FindName("RootBorder")!;
        if (border.Background is SolidColorBrush brush)
            return (brush.Color.A, brush.Color.R, brush.Color.G, brush.Color.B);
        return (0, 0, 0, 0);
    }

    private static ResourceDictionary? GetThemeDictionary(OverlayWindow window)
    {
        foreach (var dict in window.Resources.MergedDictionaries)
        {
            if (OverlayThemeResources.IsManagedThemeSource(dict.Source?.OriginalString))
                return dict;
        }
        return null;
    }

    private static void AssertAppearanceStable(OverlayWindow window, (byte A, byte R, byte G, byte B) expected, double expectedOpacity)
    {
        Assert.Equal(expected, GetBorderArgb(window));
        Assert.Equal(expectedOpacity, window.Opacity);
    }

    public static IEnumerable<object[]> ThemeOpacityCases()
    {
        yield return new object[] { (int)EffectiveTheme.Dark, OverlayOpacityPreference.Percent55 };
        yield return new object[] { (int)EffectiveTheme.Light, OverlayOpacityPreference.Percent55 };
        yield return new object[] { (int)EffectiveTheme.Dark, OverlayOpacityPreference.Default };
        yield return new object[] { (int)EffectiveTheme.Light, OverlayOpacityPreference.Percent70 };
    }

    [Theory]
    [MemberData(nameof(ThemeOpacityCases))]
    public void ApplyTheme_RepeatedSameTheme_PreservesResourceDictionaryInstance(int themeInt, OverlayOpacityPreference opacity)
    {
        var theme = (EffectiveTheme)themeInt;
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateWindow();
            window.ApplyOpacity(opacity);
            window.ApplyTheme(theme);

            var dictBefore = GetThemeDictionary(window);
            var appearanceBefore = GetBorderArgb(window);
            double opacityBefore = window.Opacity;

            Assert.NotNull(dictBefore);

            window.ApplyTheme(theme);

            var dictAfter = GetThemeDictionary(window);
            Assert.Same(dictBefore, dictAfter);
            AssertAppearanceStable(window, appearanceBefore, opacityBefore);
        });
    }

    [Theory]
    [MemberData(nameof(ThemeOpacityCases))]
    public void UpdateSettings_RepeatedSameSettings_AppearanceStable(int themeInt, OverlayOpacityPreference opacity)
    {
        var theme = (EffectiveTheme)themeInt;
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateWindow();
            window.ApplyTheme(theme);

            var settings = TraySettings.Default with { OverlayOpacity = opacity, OverlayLocked = false };
            window.UpdateSettings(settings);
            var appearance = GetBorderArgb(window);
            double winOpacity = window.Opacity;

            window.UpdateSettings(settings);
            window.UpdateSettings(settings);

            AssertAppearanceStable(window, appearance, winOpacity);
        });
    }

    [Fact]
    public void UpdateSettings_LockUnlock_AppearanceStable()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateWindow();
            window.ApplyTheme(EffectiveTheme.Dark);
            window.ApplyOpacity(OverlayOpacityPreference.Percent55);
            var appearance = GetBorderArgb(window);
            double winOpacity = window.Opacity;

            window.UpdateSettings(TraySettings.Default with { OverlayOpacity = OverlayOpacityPreference.Percent55, OverlayLocked = true });
            AssertAppearanceStable(window, appearance, winOpacity);

            window.UpdateSettings(TraySettings.Default with { OverlayOpacity = OverlayOpacityPreference.Percent55, OverlayLocked = false });
            AssertAppearanceStable(window, appearance, winOpacity);
        });
    }

    [Fact]
    public void PositionAndContentChanges_AppearanceStable()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateWindow();
            window.ApplyTheme(EffectiveTheme.Dark);
            window.ApplyOpacity(OverlayOpacityPreference.Percent55);
            var appearance = GetBorderArgb(window);
            double winOpacity = window.Opacity;

            window.MoveToPosition(500, 300);
            AssertAppearanceStable(window, appearance, winOpacity);

            window.ResetPosition(TraySettings.Default with
            {
                SharedPosition = new OverlayPositionState.Preset(OverlayPositionPreset.BottomRight)
            });
            AssertAppearanceStable(window, appearance, winOpacity);

            window.UpdateContent(new[] { "A", "B", "C", "D", "E", "F" });
            AssertAppearanceStable(window, appearance, winOpacity);
        });
    }

    [Fact]
    public void ApplyTheme_LightToDark_ArgbChanges()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateWindow();
            window.ApplyOpacity(OverlayOpacityPreference.Percent55);
            window.ApplyTheme(EffectiveTheme.Light);
            var light = GetBorderArgb(window);

            window.ApplyTheme(EffectiveTheme.Dark);
            var dark = GetBorderArgb(window);

            Assert.NotEqual(light, dark);
            Assert.Equal(light.A, dark.A);
            Assert.NotEqual(light.R, dark.R);
        });
    }

    [Fact]
    public void ApplyOpacity_Change_AlphaChanges()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateWindow();
            window.ApplyTheme(EffectiveTheme.Dark);
            window.ApplyOpacity(OverlayOpacityPreference.Percent55);
            var before = GetBorderArgb(window);

            window.ApplyOpacity(OverlayOpacityPreference.Percent70);
            var after = GetBorderArgb(window);

            Assert.NotEqual(before.A, after.A);
            Assert.Equal(before.R, after.R);
        });
    }
}
