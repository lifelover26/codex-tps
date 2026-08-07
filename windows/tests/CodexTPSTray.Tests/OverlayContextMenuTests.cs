using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using CodexTPSCore;
using Xunit;
using WpfMenuItem = System.Windows.Controls.MenuItem;
using WpfContextMenu = System.Windows.Controls.ContextMenu;

namespace CodexTPSTray.Tests;

[Collection("TrayIconManagerTests")]
public class OverlayContextMenuTests
{
    private sealed class FakeSettingsStore : ITraySettingsStore
    {
        private TraySettings _settings;
        public List<TraySettings> SavedSettings { get; } = new();

        public FakeSettingsStore(TraySettings settings) => _settings = settings;

        public TraySettings Load() => _settings;
        public bool TrySave(TraySettings settings)
        {
            _settings = settings;
            SavedSettings.Add(settings);
            return true;
        }
    }

    private sealed class FakeWindowNativeInterop : OverlayWindow.IWindowNativeInterop
    {
        public IntPtr GetHandle(Window window) => new(0x1234);
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

    private sealed class CapturingMenuCommandHandler : IOverlayMenuCommandHandler
    {
        public List<bool> ToggleOverlayCalls { get; } = new();
        public List<bool> ToggleLockCalls { get; } = new();
        public List<OverlayPositionPreset> SelectPositionCalls { get; } = new();
        public List<OverlayOpacityPreference> SelectOpacityCalls { get; } = new();
        public List<OverlayThemePreference> SelectThemeCalls { get; } = new();

        public void ToggleOverlay(bool enabled) => ToggleOverlayCalls.Add(enabled);
        public void ToggleLock(bool locked) => ToggleLockCalls.Add(locked);
        public void SelectPosition(OverlayPositionPreset preset) => SelectPositionCalls.Add(preset);
        public void SelectOpacity(OverlayOpacityPreference opacity) => SelectOpacityCalls.Add(opacity);
        public void SelectTheme(OverlayThemePreference theme) => SelectThemeCalls.Add(theme);
    }

    private sealed class TestMenuStateProvider : IOverlayMenuStateProvider
    {
        public TraySettings CurrentSettings { get; set; } = TraySettings.Default;
        public bool IsOverlayVisible { get; set; } = true;
    }

    private static OverlayWindow CreateTestWindow(IOverlayMenuCommandHandler? handler = null, IOverlayMenuStateProvider? stateProvider = null)
    {
        var window = new OverlayWindow(new FakeWorkAreaProvider(), new FakeWindowNativeInterop());
        if (handler != null) window.SetOverlayMenuCommandHandler(handler);
        if (stateProvider != null) window.SetOverlayMenuStateProvider(stateProvider);
        return window;
    }

    private static WpfContextMenu GetContextMenu(OverlayWindow window)
    {
        var border = (Border)window.FindName("RootBorder")!;
        return border.ContextMenu!;
    }

    [Fact]
    public void OverlayMenuDefinition_PositionPresets_MatchesEnumValues()
    {
        var expected = Enum.GetValues<OverlayPositionPreset>().ToList();
        Assert.Equal(expected, OverlayMenuDefinition.PositionPresets);
    }

    [Fact]
    public void OverlayMenuDefinition_OpacityPreferences_MatchesEnumValues()
    {
        var expected = Enum.GetValues<OverlayOpacityPreference>().ToList();
        Assert.Equal(expected, OverlayMenuDefinition.OpacityPreferences);
    }

    [Fact]
    public void OverlayMenuDefinition_ThemePreferences_MatchesEnumValues()
    {
        var expected = Enum.GetValues<OverlayThemePreference>().ToList();
        Assert.Equal(expected, OverlayMenuDefinition.ThemePreferences);
    }

    [Fact]
    public void Menu_Unlocked_IsEnabled()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = false, OverlayEnabled = true },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            var cm = GetContextMenu(window);
            Assert.True(cm.IsEnabled);
        });
    }

    [Fact]
    public void Menu_Locked_IsDisabled()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = true, OverlayEnabled = true },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);
            window.UpdateContextMenuState();

            var cm = GetContextMenu(window);
            Assert.False(cm.IsEnabled);
        });
    }

    [Fact]
    public void LockStateChange_MenuClosesAndDisables()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = false, OverlayEnabled = true },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);
            window.UpdateContextMenuState();

            var cm = GetContextMenu(window);
            cm.IsOpen = true;
            Assert.True(cm.IsEnabled);

            window.UpdateSettings(stateProvider.CurrentSettings with { OverlayLocked = true });

            Assert.False(cm.IsOpen);
            Assert.False(cm.IsEnabled);
        });
    }

    [Fact]
    public void UnlockStateChange_MenuReEnables()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = true, OverlayEnabled = true },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);

            var cm = GetContextMenu(window);
            Assert.False(cm.IsEnabled);

            window.UpdateSettings(stateProvider.CurrentSettings with { OverlayLocked = false });
            window.UpdateContextMenuState();

            Assert.True(cm.IsEnabled);
        });
    }

    [Fact]
    public void MenuCommand_Locked_DoesNotInvokeHandler()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var handler = new CapturingMenuCommandHandler();
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = true },
                IsOverlayVisible = false
            };
            var window = CreateTestWindow(handler, stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);

            var showItem = (WpfMenuItem)window.FindName("ShowOverlayMenuItem")!;
            showItem.IsChecked = true;
            showItem.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));

            Assert.Empty(handler.ToggleOverlayCalls);
            Assert.Empty(handler.ToggleLockCalls);
            Assert.Empty(handler.SelectPositionCalls);
            Assert.Empty(handler.SelectOpacityCalls);
            Assert.Empty(handler.SelectThemeCalls);
        });
    }

    [Fact]
    public void MenuCommand_Unlocked_ShowOverlay_Toggles()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var handler = new CapturingMenuCommandHandler();
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = false, OverlayEnabled = false },
                IsOverlayVisible = false
            };
            var window = CreateTestWindow(handler, stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);
            window.UpdateContextMenuState();

            var showItem = (WpfMenuItem)window.FindName("ShowOverlayMenuItem")!;
            showItem.IsChecked = true;
            showItem.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));

            Assert.Single(handler.ToggleOverlayCalls);
            Assert.True(handler.ToggleOverlayCalls[0]);
        });
    }

    [Fact]
    public void MenuCommand_Unlocked_LockOverlay_Toggles()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var handler = new CapturingMenuCommandHandler();
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = false },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(handler, stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);
            window.UpdateContextMenuState();

            var lockItem = (WpfMenuItem)window.FindName("LockOverlayMenuItem")!;
            lockItem.IsChecked = true;
            lockItem.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));

            Assert.Single(handler.ToggleLockCalls);
            Assert.True(handler.ToggleLockCalls[0]);
        });
    }

    [Fact]
    public void MenuCommand_Unlocked_PositionSelect_InvokesHandler()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var handler = new CapturingMenuCommandHandler();
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = false, OverlayPosition = OverlayPositionPreset.TopLeft },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(handler, stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);
            window.UpdateContextMenuState();

            var brItem = (WpfMenuItem)window.FindName("PositionBottomRight")!;
            brItem.IsChecked = true;
            brItem.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));

            Assert.Single(handler.SelectPositionCalls);
            Assert.Equal(OverlayPositionPreset.BottomRight, handler.SelectPositionCalls[0]);
        });
    }

    [Fact]
    public void MenuCommand_Unlocked_OpacitySelect_InvokesHandler()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var handler = new CapturingMenuCommandHandler();
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = false, OverlayOpacity = OverlayOpacityPreference.Default },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(handler, stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);
            window.UpdateContextMenuState();

            var opItem = (WpfMenuItem)window.FindName("Opacity70")!;
            opItem.IsChecked = true;
            opItem.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));

            Assert.Single(handler.SelectOpacityCalls);
            Assert.Equal(OverlayOpacityPreference.Percent70, handler.SelectOpacityCalls[0]);
        });
    }

    [Fact]
    public void MenuCommand_Unlocked_ThemeSelect_InvokesHandler()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var handler = new CapturingMenuCommandHandler();
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = false, OverlayTheme = OverlayThemePreference.FollowApplication },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(handler, stateProvider);
            window.UpdateSettings(stateProvider.CurrentSettings);
            window.UpdateContextMenuState();

            var darkItem = (WpfMenuItem)window.FindName("ThemeDark")!;
            darkItem.IsChecked = true;
            darkItem.RaiseEvent(new RoutedEventArgs(WpfMenuItem.ClickEvent));

            Assert.Single(handler.SelectThemeCalls);
            Assert.Equal(OverlayThemePreference.Dark, handler.SelectThemeCalls[0]);
        });
    }

    [Fact]
    public void MenuItemOrder_MatchesExpectedStructure()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var cm = GetContextMenu(window);
            var items = cm.Items.Cast<object>().ToList();

            Assert.Equal(6, items.Count);
            Assert.IsType<WpfMenuItem>(items[0]);
            Assert.Equal("ShowOverlayMenuItem", ((WpfMenuItem)items[0]).Name);
            Assert.IsType<WpfMenuItem>(items[1]);
            Assert.Equal("LockOverlayMenuItem", ((WpfMenuItem)items[1]).Name);
            Assert.IsType<Separator>(items[2]);
            Assert.IsType<WpfMenuItem>(items[3]);
            Assert.Equal("PositionSubmenu", ((WpfMenuItem)items[3]).Name);
            Assert.IsType<WpfMenuItem>(items[4]);
            Assert.Equal("OpacitySubmenu", ((WpfMenuItem)items[4]).Name);
            Assert.IsType<WpfMenuItem>(items[5]);
            Assert.Equal("ThemeSubmenu", ((WpfMenuItem)items[5]).Name);
        });
    }

    [Fact]
    public void PositionSubmenu_HasAllSixPresetsInOrder()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var posSub = (WpfMenuItem)window.FindName("PositionSubmenu")!;
            var children = posSub.Items.Cast<WpfMenuItem>().ToList();

            Assert.Equal(OverlayMenuDefinition.PositionPresets.Count, children.Count);
            for (int i = 0; i < OverlayMenuDefinition.PositionPresets.Count; i++)
            {
                var expectedName = GetPositionItemName(OverlayMenuDefinition.PositionPresets[i]);
                Assert.Equal(expectedName, children[i].Name);
            }
        });
    }

    [Fact]
    public void OpacitySubmenu_HasAllSixOptionsInOrder()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var opSub = (WpfMenuItem)window.FindName("OpacitySubmenu")!;
            var children = opSub.Items.Cast<WpfMenuItem>().ToList();

            Assert.Equal(OverlayMenuDefinition.OpacityPreferences.Count, children.Count);
            for (int i = 0; i < OverlayMenuDefinition.OpacityPreferences.Count; i++)
            {
                var expectedName = GetOpacityItemName(OverlayMenuDefinition.OpacityPreferences[i]);
                Assert.Equal(expectedName, children[i].Name);
            }
        });
    }

    [Fact]
    public void ThemeSubmenu_HasAllFourOptionsInOrder()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var themeSub = (WpfMenuItem)window.FindName("ThemeSubmenu")!;
            var children = themeSub.Items.Cast<WpfMenuItem>().ToList();

            Assert.Equal(OverlayMenuDefinition.ThemePreferences.Count, children.Count);
            for (int i = 0; i < OverlayMenuDefinition.ThemePreferences.Count; i++)
            {
                var expectedName = GetThemeItemName(OverlayMenuDefinition.ThemePreferences[i]);
                Assert.Equal(expectedName, children[i].Name);
            }
        });
    }

    [Fact]
    public void CheckState_ShowOverlay_SyncsWithStateProvider()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayEnabled = true },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            var showItem = (WpfMenuItem)window.FindName("ShowOverlayMenuItem")!;
            Assert.True(showItem.IsChecked);

            stateProvider.CurrentSettings = stateProvider.CurrentSettings with { OverlayEnabled = false };
            window.UpdateContextMenuState();
            Assert.False(showItem.IsChecked);
        });
    }

    [Fact]
    public void CheckState_Lock_SyncsWithStateProvider()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayLocked = false },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            var lockItem = (WpfMenuItem)window.FindName("LockOverlayMenuItem")!;
            Assert.False(lockItem.IsChecked);

            stateProvider.CurrentSettings = stateProvider.CurrentSettings with { OverlayLocked = true };
            window.UpdateContextMenuState();
            Assert.True(lockItem.IsChecked);
        });
    }

    [Fact]
    public void CheckState_AllPositionPresets_SyncCheckmarks()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            foreach (var targetPreset in OverlayMenuDefinition.PositionPresets)
            {
                var stateProvider = new TestMenuStateProvider
                {
                    CurrentSettings = TraySettings.Default with { OverlayPosition = targetPreset },
                    IsOverlayVisible = true
                };
                var window = CreateTestWindow(stateProvider: stateProvider);
                window.UpdateContextMenuState();

                foreach (var preset in OverlayMenuDefinition.PositionPresets)
                {
                    var item = GetPositionItem(window, preset);
                    Assert.Equal(preset == targetPreset, item.IsChecked);
                }
            }
        });
    }

    [Fact]
    public void CheckState_AllOpacityOptions_SyncCheckmarks()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            foreach (var targetOpacity in OverlayMenuDefinition.OpacityPreferences)
            {
                var stateProvider = new TestMenuStateProvider
                {
                    CurrentSettings = TraySettings.Default with { OverlayOpacity = targetOpacity },
                    IsOverlayVisible = true
                };
                var window = CreateTestWindow(stateProvider: stateProvider);
                window.UpdateContextMenuState();

                foreach (var opacity in OverlayMenuDefinition.OpacityPreferences)
                {
                    var item = GetOpacityItem(window, opacity);
                    Assert.Equal(opacity == targetOpacity, item.IsChecked);
                }
            }
        });
    }

    [Fact]
    public void CheckState_AllThemeOptions_SyncCheckmarks()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            foreach (var targetTheme in OverlayMenuDefinition.ThemePreferences)
            {
                var stateProvider = new TestMenuStateProvider
                {
                    CurrentSettings = TraySettings.Default with { OverlayTheme = targetTheme },
                    IsOverlayVisible = true
                };
                var window = CreateTestWindow(stateProvider: stateProvider);
                window.UpdateContextMenuState();

                foreach (var theme in OverlayMenuDefinition.ThemePreferences)
                {
                    var item = GetThemeItem(window, theme);
                    Assert.Equal(theme == targetTheme, item.IsChecked);
                }
            }
        });
    }

    [Fact]
    public void Localization_English_ShowsEnglishText()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { Language = Language.English },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            Assert.Equal("Show Overlay", ((WpfMenuItem)window.FindName("ShowOverlayMenuItem")!).Header);
            Assert.Equal("Lock Overlay", ((WpfMenuItem)window.FindName("LockOverlayMenuItem")!).Header);
            Assert.Equal("Position", ((WpfMenuItem)window.FindName("PositionSubmenu")!).Header);
            Assert.Equal("Background Opacity", ((WpfMenuItem)window.FindName("OpacitySubmenu")!).Header);
            Assert.Equal("Theme", ((WpfMenuItem)window.FindName("ThemeSubmenu")!).Header);
        });
    }

    [Fact]
    public void Localization_Chinese_ShowsChineseText()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { Language = Language.Chinese },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            Assert.Equal("显示悬浮窗", ((WpfMenuItem)window.FindName("ShowOverlayMenuItem")!).Header);
            Assert.Equal("锁定悬浮窗", ((WpfMenuItem)window.FindName("LockOverlayMenuItem")!).Header);
            Assert.Equal("快速定位", ((WpfMenuItem)window.FindName("PositionSubmenu")!).Header);
            Assert.Equal("背景不透明度", ((WpfMenuItem)window.FindName("OpacitySubmenu")!).Header);
            Assert.Equal("主题", ((WpfMenuItem)window.FindName("ThemeSubmenu")!).Header);
        });
    }

    [Fact]
    public void Localization_SwitchLanguage_UpdatesText()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { Language = Language.English },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            var showItem = (WpfMenuItem)window.FindName("ShowOverlayMenuItem")!;
            Assert.Equal("Show Overlay", showItem.Header);

            stateProvider.CurrentSettings = stateProvider.CurrentSettings with { Language = Language.Chinese };
            window.UpdateContextMenuState();
            Assert.Equal("显示悬浮窗", showItem.Header);
        });
    }

    [Fact]
    public void Localization_English_PositionItemsShowCorrectText()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { Language = Language.English },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            foreach (var preset in OverlayMenuDefinition.PositionPresets)
            {
                var item = GetPositionItem(window, preset);
                var expected = Localization.GetPositionPresetDisplayName(preset, Language.English);
                Assert.Equal(expected, item.Header);
            }
        });
    }

    [Fact]
    public void Localization_Chinese_PositionItemsShowCorrectText()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { Language = Language.Chinese },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            foreach (var preset in OverlayMenuDefinition.PositionPresets)
            {
                var item = GetPositionItem(window, preset);
                var expected = Localization.GetPositionPresetDisplayName(preset, Language.Chinese);
                Assert.Equal(expected, item.Header);
            }
        });
    }

    [Fact]
    public void MenuResources_DarkTheme_ContainsAllMenuBrushes()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            window.ApplyTheme(EffectiveTheme.Dark);

            Assert.NotNull(window.FindResource("OverlayMenuBackgroundBrush"));
            Assert.NotNull(window.FindResource("OverlayMenuBorderBrush"));
            Assert.NotNull(window.FindResource("OverlayMenuItemHighlightBrush"));
            Assert.NotNull(window.FindResource("OverlaySeparatorBrush"));
            Assert.IsType<System.Windows.Media.SolidColorBrush>(window.FindResource("OverlayMenuBackgroundBrush"));
        });
    }

    [Fact]
    public void MenuResources_LightTheme_ContainsAllMenuBrushes()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            window.ApplyTheme(EffectiveTheme.Light);

            Assert.NotNull(window.FindResource("OverlayMenuBackgroundBrush"));
            Assert.NotNull(window.FindResource("OverlayMenuBorderBrush"));
            Assert.NotNull(window.FindResource("OverlayMenuItemHighlightBrush"));
            Assert.NotNull(window.FindResource("OverlaySeparatorBrush"));
            Assert.IsType<System.Windows.Media.SolidColorBrush>(window.FindResource("OverlayMenuBackgroundBrush"));
        });
    }

    [Fact]
    public void MenuResources_DarkVsLight_BackgroundColorsDiffer()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();

            window.ApplyTheme(EffectiveTheme.Dark);
            var darkBg = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuBackgroundBrush");

            window.ApplyTheme(EffectiveTheme.Light);
            var lightBg = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuBackgroundBrush");

            Assert.NotEqual(darkBg.Color, lightBg.Color);
        });
    }

    [Fact]
    public void MenuTemplate_ContextMenuUsesExplicitControlTemplate()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var cm = GetContextMenu(window);

            Assert.NotNull(cm.Template);
            Assert.Equal(typeof(System.Windows.Controls.ContextMenu), cm.Template.TargetType);
        });
    }

    [Fact]
    public void MenuTemplate_MenuItemUsesExplicitControlTemplate()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var showItem = (WpfMenuItem)window.FindName("ShowOverlayMenuItem")!;

            Assert.NotNull(showItem.Template);
            Assert.Equal(typeof(WpfMenuItem), showItem.Template.TargetType);
        });
    }

    [Fact]
    public void MenuTemplate_SubmenuItemsUseSameExplicitTemplate()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var positionSubmenu = (WpfMenuItem)window.FindName("PositionSubmenu")!;
            var topLevelTemplate = positionSubmenu.Template;

            foreach (var item in positionSubmenu.Items.Cast<WpfMenuItem>())
            {
                Assert.NotNull(item.Template);
                Assert.Same(topLevelTemplate, item.Template);
            }

            var themeSubmenu = (WpfMenuItem)window.FindName("ThemeSubmenu")!;
            foreach (var item in themeSubmenu.Items.Cast<WpfMenuItem>())
            {
                Assert.NotNull(item.Template);
                Assert.Same(topLevelTemplate, item.Template);
            }
        });
    }

    [Fact]
    public void MenuTemplate_ContextMenuTemplateResourceExists()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var template = window.TryFindResource("OverlayContextMenuTemplate") as System.Windows.Controls.ControlTemplate;

            Assert.NotNull(template);
            Assert.Equal(typeof(System.Windows.Controls.ContextMenu), template.TargetType);
        });
    }

    [Fact]
    public void MenuTemplate_MenuItemTemplateResourceExists()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var template = window.TryFindResource("OverlayMenuItemTemplate") as System.Windows.Controls.ControlTemplate;

            Assert.NotNull(template);
            Assert.Equal(typeof(WpfMenuItem), template.TargetType);
        });
    }

    [Fact]
    public void MenuResources_DarkTheme_MenuBackgroundIsDark()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            window.ApplyTheme(EffectiveTheme.Dark);

            var menuBg = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuBackgroundBrush");
            var checkBg = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuCheckBackgroundBrush");

            Assert.True(IsDarkColor(menuBg.Color), "Menu background should be dark");
            Assert.True(IsDarkColor(checkBg.Color), "Check column background should be dark");
        });
    }

    [Fact]
    public void MenuResources_LightTheme_MenuBackgroundIsLight()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            window.ApplyTheme(EffectiveTheme.Light);

            var menuBg = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuBackgroundBrush");
            var checkBg = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuCheckBackgroundBrush");

            Assert.True(IsLightColor(menuBg.Color), "Menu background should be light");
            Assert.True(IsLightColor(checkBg.Color), "Check column background should be light");
        });
    }

    [Fact]
    public void MenuResources_DarkTheme_AllRequiredBrushesExist()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            window.ApplyTheme(EffectiveTheme.Dark);

            string[] requiredKeys =
            [
                "OverlayMenuBackgroundBrush",
                "OverlayMenuBorderBrush",
                "OverlayMenuItemHighlightBrush",
                "OverlayMenuItemHighlightTextBrush",
                "OverlayMenuItemDisabledTextBrush",
                "OverlayMenuCheckBackgroundBrush",
                "OverlayMenuGlyphBrush",
                "OverlayMenuSubmenuGlyphBrush",
                "OverlaySeparatorBrush"
            ];

            foreach (var key in requiredKeys)
            {
                var brush = window.FindResource(key);
                Assert.IsType<System.Windows.Media.SolidColorBrush>(brush);
            }
        });
    }

    [Fact]
    public void MenuResources_DarkTheme_TextHasContrastWithBackground()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            window.ApplyTheme(EffectiveTheme.Dark);

            var bg = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuBackgroundBrush");
            var text = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayPrimaryTextBrush");

            double contrast = CalculateContrastRatio(bg.Color, text.Color);
            Assert.True(contrast >= 4.5, $"Text contrast ratio should be at least 4.5:1, got {contrast:F2}:1");
        });
    }

    [Fact]
    public void MenuResources_LightTheme_TextHasContrastWithBackground()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            window.ApplyTheme(EffectiveTheme.Light);

            var bg = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuBackgroundBrush");
            var text = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayPrimaryTextBrush");

            double contrast = CalculateContrastRatio(bg.Color, text.Color);
            Assert.True(contrast >= 4.5, $"Text contrast ratio should be at least 4.5:1, got {contrast:F2}:1");
        });
    }

    [Fact]
    public void MenuResources_DarkTheme_HighlightTextHasContrast()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            window.ApplyTheme(EffectiveTheme.Dark);

            var highlight = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuItemHighlightBrush");
            var highlightText = (System.Windows.Media.SolidColorBrush)window.FindResource("OverlayMenuItemHighlightTextBrush");

            double contrast = CalculateContrastRatio(highlight.Color, highlightText.Color);
            Assert.True(contrast >= 4.5, $"Highlight text contrast ratio should be at least 4.5:1, got {contrast:F2}:1");
        });
    }

    [Fact]
    public void MenuStructure_SeparatorCountMatchesTrayOverlayMenu()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var cm = GetContextMenu(window);
            var separators = cm.Items.Cast<object>().OfType<System.Windows.Controls.Separator>().ToList();

            Assert.Single(separators);
        });
    }

    [Fact]
    public void MenuStructure_SeparatorPositionMatchesTrayOverlayMenu()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var cm = GetContextMenu(window);
            var items = cm.Items.Cast<object>().ToList();

            Assert.IsType<System.Windows.Controls.Separator>(items[2]);
            Assert.IsNotType<System.Windows.Controls.Separator>(items[0]);
            Assert.IsNotType<System.Windows.Controls.Separator>(items[1]);
            Assert.IsNotType<System.Windows.Controls.Separator>(items[3]);
            Assert.IsNotType<System.Windows.Controls.Separator>(items[4]);
            Assert.IsNotType<System.Windows.Controls.Separator>(items[5]);
        });
    }

    [Fact]
    public void MenuStructure_NoMinWidthOrFixedPadding()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var cm = GetContextMenu(window);

            Assert.False(cm.MinWidth > 0, "ContextMenu should not have fixed MinWidth");
            Assert.True(cm.Padding.Left <= 2, "ContextMenu padding should be small to match tray menu width");
            Assert.True(cm.Padding.Right <= 2, "ContextMenu padding should be small to match tray menu width");
        });
    }

    [Fact]
    public void MenuStructure_ItemPaddingIsCompact()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var window = CreateTestWindow();
            var showItem = (WpfMenuItem)window.FindName("ShowOverlayMenuItem")!;

            Assert.True(showItem.Padding.Left <= 6, "MenuItem padding should be compact like WinForms");
            Assert.True(showItem.Padding.Right <= 6, "MenuItem padding should be compact like WinForms");
            Assert.True(showItem.Padding.Top <= 3, "MenuItem vertical padding should be compact");
            Assert.True(showItem.Padding.Bottom <= 3, "MenuItem vertical padding should be compact");
        });
    }

    [Fact]
    public void PositionItems_DisabledWhenOverlayHidden()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayEnabled = false },
                IsOverlayVisible = false
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            foreach (var preset in OverlayMenuDefinition.PositionPresets)
            {
                var item = GetPositionItem(window, preset);
                Assert.False(item.IsEnabled);
            }
        });
    }

    [Fact]
    public void PositionItems_EnabledWhenOverlayVisible()
    {
        WpfTestHelpers.RunInStaWithWpf(() =>
        {
            var stateProvider = new TestMenuStateProvider
            {
                CurrentSettings = TraySettings.Default with { OverlayEnabled = true },
                IsOverlayVisible = true
            };
            var window = CreateTestWindow(stateProvider: stateProvider);
            window.UpdateContextMenuState();

            foreach (var preset in OverlayMenuDefinition.PositionPresets)
            {
                var item = GetPositionItem(window, preset);
                Assert.True(item.IsEnabled);
            }
        });
    }

    [Fact]
    public Task TrayLockMenuItem_ExistsAndIsCheckable()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with { OverlayLocked = false };
            var store = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                store,
                settings,
                new SessionScanner(),
                new FakeShellLauncher(),
                new FakeRunKeyStore(),
                () => null,
                new WpfMonitorWorkAreaProvider(),
                new WindowsFormsThemeApplier(),
                new ThemeResolver(new WindowsSystemThemeSource())
            );
            manager.Start();
            await manager.StartupTask;

            var lockItem = manager.LockOverlayMenuItemForTest;
            Assert.NotNull(lockItem);
            Assert.True(lockItem.CheckOnClick);
        });
    }

    [Fact]
    public Task TrayShowOverlayMenuItem_ExistsAndIsCheckable()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with { OverlayEnabled = false };
            var store = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                store,
                settings,
                new SessionScanner(),
                new FakeShellLauncher(),
                new FakeRunKeyStore(),
                () => null,
                new WpfMonitorWorkAreaProvider(),
                new WindowsFormsThemeApplier(),
                new ThemeResolver(new WindowsSystemThemeSource())
            );
            manager.Start();
            await manager.StartupTask;

            var showItem = manager.ShowOverlayMenuItemForTest;
            Assert.NotNull(showItem);
            Assert.True(showItem.CheckOnClick);
        });
    }

    [Fact]
    public Task TrayLockUnlock_FullRoundTrip_SavesSettings()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with { OverlayLocked = false, OverlayEnabled = true };
            var store = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                store,
                settings,
                new SessionScanner(),
                new FakeShellLauncher(),
                new FakeRunKeyStore(),
                () => null,
                new WpfMonitorWorkAreaProvider(),
                new WindowsFormsThemeApplier(),
                new ThemeResolver(new WindowsSystemThemeSource())
            );
            manager.Start();
            await manager.StartupTask;

            var lockItem = manager.LockOverlayMenuItemForTest;

            Assert.False(lockItem.Checked);
            lockItem.PerformClick();
            Assert.True(lockItem.Checked);

            var lastSaved = store.SavedSettings.Last();
            Assert.True(lastSaved.OverlayLocked);

            lockItem.PerformClick();
            Assert.False(lockItem.Checked);
            lastSaved = store.SavedSettings.Last();
            Assert.False(lastSaved.OverlayLocked);
        });
    }

    [Fact]
    public Task TrayShowHide_FullRoundTrip_SavesSettings()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with { OverlayEnabled = false };
            var store = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                store,
                settings,
                new SessionScanner(),
                new FakeShellLauncher(),
                new FakeRunKeyStore(),
                () => null,
                new WpfMonitorWorkAreaProvider(),
                new WindowsFormsThemeApplier(),
                new ThemeResolver(new WindowsSystemThemeSource())
            );
            manager.Start();
            await manager.StartupTask;

            var showItem = manager.ShowOverlayMenuItemForTest;

            Assert.False(showItem.Checked);
            showItem.PerformClick();
            Assert.True(showItem.Checked);

            var lastSaved = store.SavedSettings.Last();
            Assert.True(lastSaved.OverlayEnabled);

            showItem.PerformClick();
            Assert.False(showItem.Checked);
            lastSaved = store.SavedSettings.Last();
            Assert.False(lastSaved.OverlayEnabled);
        });
    }

    private static string GetPositionItemName(OverlayPositionPreset preset) => preset switch
    {
        OverlayPositionPreset.TopLeft => "PositionTopLeft",
        OverlayPositionPreset.TopRight => "PositionTopRight",
        OverlayPositionPreset.MiddleLeft => "PositionMiddleLeft",
        OverlayPositionPreset.MiddleRight => "PositionMiddleRight",
        OverlayPositionPreset.BottomLeft => "PositionBottomLeft",
        OverlayPositionPreset.BottomRight => "PositionBottomRight",
        _ => throw new ArgumentOutOfRangeException(nameof(preset))
    };

    private static string GetOpacityItemName(OverlayOpacityPreference opacity) => opacity switch
    {
        OverlayOpacityPreference.Default => "OpacityDefault",
        OverlayOpacityPreference.Percent40 => "Opacity40",
        OverlayOpacityPreference.Percent55 => "Opacity55",
        OverlayOpacityPreference.Percent70 => "Opacity70",
        OverlayOpacityPreference.Percent85 => "Opacity85",
        OverlayOpacityPreference.Opaque => "OpacityOpaque",
        _ => throw new ArgumentOutOfRangeException(nameof(opacity))
    };

    private static string GetThemeItemName(OverlayThemePreference theme) => theme switch
    {
        OverlayThemePreference.FollowApplication => "ThemeFollowApp",
        OverlayThemePreference.System => "ThemeSystem",
        OverlayThemePreference.Light => "ThemeLight",
        OverlayThemePreference.Dark => "ThemeDark",
        _ => throw new ArgumentOutOfRangeException(nameof(theme))
    };

    private static WpfMenuItem GetPositionItem(OverlayWindow window, OverlayPositionPreset preset)
        => (WpfMenuItem)window.FindName(GetPositionItemName(preset))!;

    private static WpfMenuItem GetOpacityItem(OverlayWindow window, OverlayOpacityPreference opacity)
        => (WpfMenuItem)window.FindName(GetOpacityItemName(opacity))!;

    private static WpfMenuItem GetThemeItem(OverlayWindow window, OverlayThemePreference theme)
        => (WpfMenuItem)window.FindName(GetThemeItemName(theme))!;

    private static bool IsDarkColor(System.Windows.Media.Color color)
    {
        double luminance = (0.299 * color.R + 0.587 * color.G + 0.114 * color.B) / 255.0;
        return luminance < 0.5;
    }

    private static bool IsLightColor(System.Windows.Media.Color color)
    {
        return !IsDarkColor(color);
    }

    private static double CalculateContrastRatio(System.Windows.Media.Color foreground, System.Windows.Media.Color background)
    {
        double l1 = GetRelativeLuminance(foreground);
        double l2 = GetRelativeLuminance(background);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double GetRelativeLuminance(System.Windows.Media.Color color)
    {
        double r = color.R / 255.0;
        double g = color.G / 255.0;
        double b = color.B / 255.0;

        r = r <= 0.03928 ? r / 12.92 : Math.Pow((r + 0.055) / 1.055, 2.4);
        g = g <= 0.03928 ? g / 12.92 : Math.Pow((g + 0.055) / 1.055, 2.4);
        b = b <= 0.03928 ? b / 12.92 : Math.Pow((b + 0.055) / 1.055, 2.4);

        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }
}
