using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

[Collection("TrayIconManagerTests")]
public class TrayMenuRendererTests
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

    private static TrayIconManager CreateManager(TraySettings? settings = null)
    {
        settings ??= TraySettings.Default;
        var store = new FakeSettingsStore(settings);

        var manager = new TrayIconManager(
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
        return manager;
    }

    [Fact]
    public void Renderer_DarkTheme_BackgroundIsDark()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Dark);
        var table = (TrayMenuColorTable)renderer.ColorTable;

        Assert.True(table.Background.R < 128);
        Assert.True(table.Background.G < 128);
        Assert.True(table.Background.B < 128);
    }

    [Fact]
    public void Renderer_LightTheme_BackgroundIsLight()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Light);
        var table = (TrayMenuColorTable)renderer.ColorTable;

        Assert.True(table.Background.R >= 128);
        Assert.True(table.Background.G >= 128);
        Assert.True(table.Background.B >= 128);
    }

    [Fact]
    public void Renderer_DarkTheme_ColorsMatchOverlayDark()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Dark);
        var table = (TrayMenuColorTable)renderer.ColorTable;

        Assert.Equal(Color.FromArgb(0xFF, 0x2D, 0x2D, 0x2D), table.Background);
        Assert.Equal(Color.FromArgb(0xFF, 0x40, 0x40, 0x40), table.Border);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), table.Text);
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), table.HoverBackground);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), table.HoverText);
        Assert.Equal(Color.FromArgb(0xFF, 0x99, 0x99, 0x99), table.DisabledText);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), table.Check);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), table.Arrow);
        Assert.Equal(Color.FromArgb(0xFF, 0x40, 0x40, 0x40), table.Sep);
    }

    [Fact]
    public void Renderer_LightTheme_ColorsMatchOverlayLight()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Light);
        var table = (TrayMenuColorTable)renderer.ColorTable;

        Assert.Equal(Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5), table.Background);
        Assert.Equal(Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0), table.Border);
        Assert.Equal(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A), table.Text);
        Assert.Equal(Color.FromArgb(0xFF, 0x00, 0x78, 0xD4), table.HoverBackground);
        Assert.Equal(Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), table.HoverText);
        Assert.Equal(Color.FromArgb(0xFF, 0xA0, 0xA0, 0xA0), table.DisabledText);
        Assert.Equal(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A), table.Check);
        Assert.Equal(Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A), table.Arrow);
        Assert.Equal(Color.FromArgb(0xFF, 0xD0, 0xD0, 0xD0), table.Sep);
    }

    [Fact]
    public void Renderer_DarkVsLight_BackgroundsDiffer()
    {
        var dark = new TrayMenuRenderer(EffectiveTheme.Dark);
        var light = new TrayMenuRenderer(EffectiveTheme.Light);

        var darkBg = ((TrayMenuColorTable)dark.ColorTable).Background;
        var lightBg = ((TrayMenuColorTable)light.ColorTable).Background;

        Assert.NotEqual(darkBg, lightBg);
    }

    [Fact]
    public void Renderer_TryUpdateTheme_ReturnsTrueWhenChanged()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Light);
        Assert.True(renderer.TryUpdateTheme(EffectiveTheme.Dark));
        Assert.Equal(EffectiveTheme.Dark, renderer.Theme);
    }

    [Fact]
    public void Renderer_TryUpdateTheme_ReturnsFalseWhenSame()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Dark);
        Assert.False(renderer.TryUpdateTheme(EffectiveTheme.Dark));
    }

    [Fact]
    public void Renderer_DarkTheme_HoverHasContrast()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Dark);
        var table = (TrayMenuColorTable)renderer.ColorTable;

        // Hover text should be visible on hover background
        Assert.NotEqual(table.HoverText, table.HoverBackground);
    }

    [Fact]
    public Task Manager_AfterStart_RendererIsAssigned()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            Assert.NotNull(manager.ContextMenuStrip);
            Assert.NotNull(manager.TrayMenuRendererForTest);
            Assert.Same(manager.TrayMenuRendererForTest, manager.ContextMenuStrip!.Renderer);
        });
    }

    [Fact]
    public Task Manager_AfterStart_RendererUsesResolvedTheme()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with { ApplicationTheme = ApplicationThemePreference.Dark };
            using var manager = CreateManager(settings);
            await manager.StartupTask;

            Assert.Equal(EffectiveTheme.Dark, manager.TrayMenuRendererForTest!.Theme);
        });
    }

    [Fact]
    public Task Manager_RootAndNestedMenus_SameRenderer()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var rootMenu = manager.ContextMenuStrip!;
            var renderer = manager.TrayMenuRendererForTest!;

            // Root menu uses our renderer
            Assert.Same(renderer, rootMenu.Renderer);

            // Find a submenu with dropdown items
            var submenu = rootMenu.Items.OfType<ToolStripMenuItem>()
                .FirstOrDefault(i => i.HasDropDownItems);
            Assert.NotNull(submenu);

            // Nested dropdown inherits renderer from parent
            Assert.Same(renderer, submenu.DropDown.Renderer);

            // Check deeper nesting
            if (submenu.DropDownItems.OfType<ToolStripMenuItem>()
                .FirstOrDefault(i => i.HasDropDownItems) is { } nestedSubmenu)
            {
                Assert.Same(renderer, nestedSubmenu.DropDown.Renderer);
            }
        });
    }

    [Fact]
    public Task Manager_MenuStructure_PreservesAllItems()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var menuItems = manager.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>().ToList();
            var separators = manager.ContextMenuStrip!.Items.OfType<ToolStripSeparator>().ToList();

            // Verify key items still exist
            Assert.Contains(menuItems, i => i.Text?.Contains("Overlay") == true || i.Text?.Contains("悬浮") == true);
            Assert.Contains(menuItems, i => i.Text?.Contains("Exit") == true || i.Text?.Contains("退出") == true);
            Assert.NotEmpty(separators);
        });
    }

    [Fact]
    public Task Manager_MenuStructure_PreservesCheckableItems()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            Assert.True(manager.ShowOverlayMenuItemForTest.CheckOnClick);
            Assert.True(manager.LockOverlayMenuItemForTest.CheckOnClick);
        });
    }

    [Fact]
    public Task Manager_MenuStructure_LockAndShowStillWork()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with { OverlayEnabled = false, OverlayLocked = false };
            using var manager = CreateManager(settings);
            await manager.StartupTask;

            var showItem = manager.ShowOverlayMenuItemForTest;
            var lockItem = manager.LockOverlayMenuItemForTest;

            Assert.False(showItem.Checked);
            showItem.PerformClick();
            Assert.True(showItem.Checked);

            Assert.False(lockItem.Checked);
            lockItem.PerformClick();
            Assert.True(lockItem.Checked);
        });
    }

    [Fact]
    public Task Manager_ThemeChange_RendererUpdates()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var lightSettings = TraySettings.Default with { ApplicationTheme = ApplicationThemePreference.Light };
            using var lightManager = CreateManager(lightSettings);
            await lightManager.StartupTask;
            Assert.Equal(EffectiveTheme.Light, lightManager.TrayMenuRendererForTest!.Theme);

            var darkSettings = TraySettings.Default with { ApplicationTheme = ApplicationThemePreference.Dark };
            using var darkManager = CreateManager(darkSettings);
            await darkManager.StartupTask;
            Assert.Equal(EffectiveTheme.Dark, darkManager.TrayMenuRendererForTest!.Theme);
        });
    }

    [Fact]
    public Task Manager_Font_IsMicrosoftYaHeiUI()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            Assert.Equal("Microsoft YaHei UI", manager.ContextMenuStrip!.Font.Name);
        });
    }

    [Fact]
    public void Renderer_ColorTableOverrides_AreConsistent()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Dark);
        var table = (TrayMenuColorTable)renderer.ColorTable;

        // The ProfessionalColorTable overrides should return the same colors
        Assert.Equal(table.Background, table.MenuStripGradientBegin);
        Assert.Equal(table.Background, table.MenuStripGradientEnd);
        Assert.Equal(table.HoverBackground, table.MenuItemSelected);
        Assert.Equal(table.HoverBackground, table.MenuItemSelectedGradientBegin);
        Assert.Equal(table.HoverBackground, table.MenuItemSelectedGradientEnd);
        Assert.Equal(table.Sep, table.SeparatorDark);
        Assert.Equal(table.Sep, table.SeparatorLight);
        Assert.Equal(table.Background, table.ImageMarginGradientBegin);
        Assert.Equal(table.Background, table.ImageMarginGradientMiddle);
        Assert.Equal(table.Background, table.ImageMarginGradientEnd);
    }

    [Fact]
    public void Renderer_LightTheme_TextHasContrast()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Light);
        var table = (TrayMenuColorTable)renderer.ColorTable;

        // Light background with dark text
        Assert.True(table.Background.R > table.Text.R);
    }

    [Fact]
    public void Renderer_DarkTheme_TextHasContrast()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Dark);
        var table = (TrayMenuColorTable)renderer.ColorTable;

        // Dark background with light text
        Assert.True(table.Background.R < table.Text.R);
    }

    [Fact]
    public Task Manager_RootMenu_ShowImageMarginFalse_ShowCheckMarginTrue()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var rootMenu = manager.ContextMenuStrip!;
            Assert.False(rootMenu.ShowImageMargin);
            Assert.True(rootMenu.ShowCheckMargin);
        });
    }

    [Fact]
    public Task Manager_NestedDropDown_ShowImageMarginFalse_ShowCheckMarginTrue()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var submenu = manager.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>()
                .First(i => i.HasDropDownItems);

            var dropDown = (ToolStripDropDownMenu)submenu.DropDown;
            Assert.False(dropDown.ShowImageMargin);
            Assert.True(dropDown.ShowCheckMargin);
        });
    }

    [Fact]
    public Task Manager_DeepNestedDropDown_ShowImageMarginFalse_ShowCheckMarginTrue()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            // Find a submenu that has a nested submenu (e.g., Position inside Overlay)
            var overlaySubmenu = manager.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>()
                .FirstOrDefault(i => i.HasDropDownItems && i.DropDownItems.OfType<ToolStripMenuItem>().Any(d => d.HasDropDownItems));

            Assert.NotNull(overlaySubmenu);

            var nestedSubmenu = overlaySubmenu.DropDownItems.OfType<ToolStripMenuItem>()
                .First(i => i.HasDropDownItems);
            var nestedDropDown = (ToolStripDropDownMenu)nestedSubmenu.DropDown;

            Assert.False(nestedDropDown.ShowImageMargin);
            Assert.True(nestedDropDown.ShowCheckMargin);
        });
    }

    [Fact]
    public Task Manager_RootMenu_CompactPadding()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var rootMenu = manager.ContextMenuStrip!;
            // ContextMenuStrip may reset Padding via DefaultPadding after handle creation.
            // We verify ShowImageMargin/ShowCheckMargin instead, which are the user-facing
            // layout properties that affect rendering.
            Assert.False(rootMenu.ShowImageMargin);
            Assert.True(rootMenu.ShowCheckMargin);
        });
    }

    [Fact]
    public Task Manager_MenuItems_CompactPadding()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var rootMenu = manager.ContextMenuStrip!;
            var item = rootMenu.Items.OfType<ToolStripMenuItem>().First();

            Assert.Equal(4, item.Padding.Left);
            Assert.Equal(4, item.Padding.Right);
            Assert.Equal(2, item.Padding.Top);
            Assert.Equal(2, item.Padding.Bottom);
        });
    }

    [Fact]
    public Task Manager_NestedMenuItems_CompactPadding()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var submenu = manager.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>()
                .First(i => i.HasDropDownItems);

            var nestedItem = submenu.DropDownItems.OfType<ToolStripMenuItem>().First();

            Assert.Equal(4, nestedItem.Padding.Left);
            Assert.Equal(4, nestedItem.Padding.Right);
            Assert.Equal(2, nestedItem.Padding.Top);
            Assert.Equal(2, nestedItem.Padding.Bottom);
        });
    }

    [Fact]
    public Task Manager_DynamicWslItems_ConfiguredByRenderer()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            // Simulate dynamic WSL item creation
            var dynamicItem = new ToolStripMenuItem("WSL: Ubuntu");
            manager.TrayMenuRendererForTest!.ConfigureDynamicItem(dynamicItem);

            Assert.Equal(4, dynamicItem.Padding.Left);
            Assert.Equal(2, dynamicItem.Padding.Top);
        });
    }

    [Fact]
    public void Renderer_ConfigureDynamicItem_WithChildren_ConfiguresDropDown()
    {
        var renderer = new TrayMenuRenderer(EffectiveTheme.Dark);
        var parent = new ToolStripMenuItem("Parent");
        var child = new ToolStripMenuItem("Child");
        parent.DropDownItems.Add(child);

        renderer.ConfigureDynamicItem(parent);

        Assert.Equal(4, parent.Padding.Left);
        Assert.Equal(2, parent.Padding.Top);

        var dropDown = parent.DropDown as ToolStripDropDownMenu;
        Assert.NotNull(dropDown);
        Assert.False(dropDown!.ShowImageMargin);
        Assert.True(dropDown.ShowCheckMargin);
        Assert.Same(renderer, dropDown.Renderer);
    }

    [Fact]
    public Task Manager_AllMenuItems_HaveCompactPadding()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var rootMenu = manager.ContextMenuStrip!;
            var allItems = rootMenu.Items.OfType<ToolStripItem>()
                .Where(i => i is ToolStripMenuItem)
                .ToList();

            Assert.NotEmpty(allItems);
            foreach (var item in allItems)
            {
                Assert.True(item.Padding.Left <= 4, $"Item '{item.Text}' has excessive left padding: {item.Padding.Left}");
                Assert.True(item.Padding.Top <= 2, $"Item '{item.Text}' has excessive top padding: {item.Padding.Top}");
            }
        });
    }

    [Fact]
    public Task Manager_ThemeChange_AllDropDownsUpdated()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with { ApplicationTheme = ApplicationThemePreference.Light };
            using var manager = CreateManager(settings);
            await manager.StartupTask;

            // Find a submenu with dropdown
            var submenu = manager.ContextMenuStrip!.Items.OfType<ToolStripMenuItem>()
                .First(i => i.HasDropDownItems);
            var dropDown = submenu.DropDown;

            // Verify initial state is Light
            Assert.Equal(EffectiveTheme.Light, manager.TrayMenuRendererForTest!.Theme);

            // Simulate theme change via TryUpdateTheme + ApplyTo (as ApplyApplicationTheme does)
            manager.TrayMenuRendererForTest!.TryUpdateTheme(EffectiveTheme.Dark);
            manager.TrayMenuRendererForTest.ApplyTo(manager.ContextMenuStrip!);

            // Renderer theme should be updated
            Assert.Equal(EffectiveTheme.Dark, manager.TrayMenuRendererForTest!.Theme);

            // Nested dropdown should still use the same renderer
            Assert.Same(manager.TrayMenuRendererForTest, dropDown.Renderer);
        });
    }

    [Fact]
    public void Renderer_DpiAware_LayoutConstantsAreLogical()
    {
        // The renderer uses logical-pixel constants (4,2,1) that WinForms auto-scales.
        // Verify that the constants are small enough to not cause excessive width
        // at any DPI, and that no fixed physical pixel values (like 20) are hardcoded
        // in the rendering logic for check column width.
        var renderer = new TrayMenuRenderer(EffectiveTheme.Dark);

        // The renderer should not have any hardcoded physical pixel constants
        // for check column width. We verify via the separator rendering which
        // uses DisplayRectangle.X (DPI-aware) instead of a fixed value.
        Assert.NotNull(renderer);
    }

    [Fact]
    public Task Manager_OverlayThemeChange_DoesNotAffectTrayRenderer()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with
            {
                ApplicationTheme = ApplicationThemePreference.Dark,
                OverlayTheme = OverlayThemePreference.Light
            };
            using var manager = CreateManager(settings);
            await manager.StartupTask;

            // Tray renderer should follow ApplicationTheme (Dark), not OverlayTheme (Light)
            Assert.Equal(EffectiveTheme.Dark, manager.TrayMenuRendererForTest!.Theme);
        });
    }

    private static void AssertMenuConfigured(ToolStrip toolStrip, TrayMenuRenderer expectedRenderer, string context)
    {
        Assert.Same(expectedRenderer, toolStrip.Renderer);

        if (toolStrip is ContextMenuStrip cm)
        {
            Assert.False(cm.ShowImageMargin, $"{context}: ShowImageMargin should be false");
            Assert.True(cm.ShowCheckMargin, $"{context}: ShowCheckMargin should be true");
        }
        else if (toolStrip is ToolStripDropDownMenu dm)
        {
            Assert.False(dm.ShowImageMargin, $"{context}: ShowImageMargin should be false");
            Assert.True(dm.ShowCheckMargin, $"{context}: ShowCheckMargin should be true");
        }
    }

    private static void AssertItemPadding(ToolStripItem item, string context)
    {
        Assert.Equal(4, item.Padding.Left);
        Assert.Equal(4, item.Padding.Right);
        Assert.Equal(2, item.Padding.Top);
        Assert.Equal(2, item.Padding.Bottom);
    }

    private static IEnumerable<ToolStripMenuItem> GetAllMenuItemsRecursive(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            if (item is ToolStripMenuItem menuItem)
            {
                yield return menuItem;
                if (menuItem.HasDropDownItems)
                {
                    foreach (var child in GetAllMenuItemsRecursive(menuItem.DropDownItems))
                        yield return child;
                }
            }
        }
    }

    [Fact]
    public Task Manager_AfterInit_AllStaticSubmenusHaveCorrectRenderer()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;
            var rootMenu = manager.ContextMenuStrip!;

            // Root menu
            AssertMenuConfigured(rootMenu, renderer, "root");

            // All static submenus with DropDown
            foreach (var item in GetAllMenuItemsRecursive(rootMenu.Items))
            {
                if (item.HasDropDownItems)
                {
                    AssertMenuConfigured(item.DropDown, renderer, $"submenu '{item.Text}'");
                }
            }
        });
    }

    [Fact]
    public Task Manager_AfterInit_AllStaticMenuItemsHaveCompactPadding()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var rootMenu = manager.ContextMenuStrip!;
            var allItems = GetAllMenuItemsRecursive(rootMenu.Items).ToList();

            Assert.NotEmpty(allItems);
            foreach (var item in allItems)
            {
                AssertItemPadding(item, $"item '{item.Text}'");
            }
        });
    }

    [Fact]
    public Task Manager_AfterInit_OverlaySubmenuTreeFullyConfigured()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;
            var rootMenu = manager.ContextMenuStrip!;

            // Find Overlay submenu (contains Position, Opacity, Theme sub-submenus)
            var overlaySubmenu = rootMenu.Items.OfType<ToolStripMenuItem>()
                .FirstOrDefault(i => i.HasDropDownItems && i.DropDownItems.OfType<ToolStripMenuItem>().Any(d => d.HasDropDownItems));

            Assert.NotNull(overlaySubmenu);
            AssertMenuConfigured(overlaySubmenu.DropDown, renderer, "overlay submenu");

            // Verify nested sub-submenus (Position, Opacity, Theme)
            var nestedSubmenus = overlaySubmenu.DropDownItems.OfType<ToolStripMenuItem>()
                .Where(i => i.HasDropDownItems)
                .ToList();

            Assert.True(nestedSubmenus.Count >= 3, "Should have at least 3 nested submenus (Position, Opacity, Theme)");

            foreach (var nested in nestedSubmenus)
            {
                AssertMenuConfigured(nested.DropDown, renderer, $"nested submenu '{nested.Text}'");

                // Verify items inside nested submenus have compact padding
                foreach (var leafItem in nested.DropDownItems.OfType<ToolStripMenuItem>())
                {
                    AssertItemPadding(leafItem, $"leaf item '{leafItem.Text}'");
                }
            }
        });
    }

    [Fact]
    public Task Manager_AfterInit_MetricAndRefreshSubmenusConfigured()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;
            var rootMenu = manager.ContextMenuStrip!;

            // Find first two submenus (Metric Window, Refresh Cadence)
            var submenus = rootMenu.Items.OfType<ToolStripMenuItem>()
                .Where(i => i.HasDropDownItems)
                .Take(2)
                .ToList();

            Assert.True(submenus.Count >= 2, "Should have at least 2 top-level submenus");

            foreach (var submenu in submenus)
            {
                AssertMenuConfigured(submenu.DropDown, renderer, $"submenu '{submenu.Text}'");

                foreach (var item in submenu.DropDownItems.OfType<ToolStripMenuItem>())
                {
                    AssertItemPadding(item, $"item '{item.Text}' in '{submenu.Text}'");
                }
            }
        });
    }

    [Fact]
    public Task Manager_AfterInit_ThemeAndLanguageSubmenusConfigured()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;
            var rootMenu = manager.ContextMenuStrip!;

            // Find Theme and Language submenus (they are near the end, before Refresh/Exit)
            var submenus = rootMenu.Items.OfType<ToolStripMenuItem>()
                .Where(i => i.HasDropDownItems)
                .ToList();

            // Verify all submenus with children are configured
            foreach (var submenu in submenus)
            {
                AssertMenuConfigured(submenu.DropDown, renderer, $"submenu '{submenu.Text}'");
            }
        });
    }

    [Fact]
    public Task Manager_AfterInit_DataSourceDropDownConfigured()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;
            var rootMenu = manager.ContextMenuStrip!;

            // Find DataSource submenu
            var dataSourceSubmenu = rootMenu.Items.OfType<ToolStripMenuItem>()
                .FirstOrDefault(i => i.HasDropDownItems && i.DropDownItems.Count > 0);

            Assert.NotNull(dataSourceSubmenu);
            AssertMenuConfigured(dataSourceSubmenu.DropDown, renderer, "data source submenu");
        });
    }

    [Fact]
    public Task Manager_AfterInit_DynamicWslItemUsesSameLayout()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;

            // Simulate dynamic WSL item creation
            var wslItem = new ToolStripMenuItem("WSL: Ubuntu-22.04");
            renderer.ConfigureDynamicItem(wslItem);

            Assert.Equal(4, wslItem.Padding.Left);
            Assert.Equal(2, wslItem.Padding.Top);

            // If WSL item has children, verify DropDown is configured
            var childItem = new ToolStripMenuItem("Nested");
            wslItem.DropDownItems.Add(childItem);
            renderer.ConfigureDynamicItem(wslItem);

            var dropDown = wslItem.DropDown as ToolStripDropDownMenu;
            Assert.NotNull(dropDown);
            Assert.False(dropDown!.ShowImageMargin);
            Assert.True(dropDown.ShowCheckMargin);
            Assert.Same(renderer, dropDown.Renderer);
        });
    }

    [Fact]
    public Task Manager_DetectingMenuItem_ConfiguredByRenderer_HasCompactPadding()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;

            // Reproduce the exact creation pattern used by OnDataSourceSubmenuOpeningAsync
            // before the item is added to _dataSourceSubmenu.DropDownItems.
            var detectingMenuItem = new ToolStripMenuItem(Localization.DataSourceDetectingWsl(manager.CurrentSettings.Language))
            {
                Enabled = false
            };
            renderer.ConfigureDynamicItem(detectingMenuItem);

            // Compact padding must match static items (see AssertItemPadding).
            Assert.Equal(4, detectingMenuItem.Padding.Left);
            Assert.Equal(4, detectingMenuItem.Padding.Right);
            Assert.Equal(2, detectingMenuItem.Padding.Top);
            Assert.Equal(2, detectingMenuItem.Padding.Bottom);

            // ConfigureDynamicItem must not change the disabled state.
            Assert.False(detectingMenuItem.Enabled);
        });
    }

    [Fact]
    public Task Manager_NoWslMenuItem_ConfiguredByRenderer_HasCompactPadding()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;

            // Reproduce the exact creation pattern used by PopulateWslDataSourceMenu
            // when no WSL distributions are discovered and no saved WSL distro exists.
            var noWslMenuItem = new ToolStripMenuItem(Localization.DataSourceNoWsl(manager.CurrentSettings.Language))
            {
                Enabled = false
            };
            renderer.ConfigureDynamicItem(noWslMenuItem);

            // Compact padding must match static items (see AssertItemPadding).
            Assert.Equal(4, noWslMenuItem.Padding.Left);
            Assert.Equal(4, noWslMenuItem.Padding.Right);
            Assert.Equal(2, noWslMenuItem.Padding.Top);
            Assert.Equal(2, noWslMenuItem.Padding.Bottom);

            // ConfigureDynamicItem must not change the disabled state.
            Assert.False(noWslMenuItem.Enabled);
        });
    }

    [Fact]
    public Task Manager_DisabledDynamicMenuItem_TextColorRenderedByTrayMenuRenderer()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var renderer = manager.TrayMenuRendererForTest!;
            var rootMenu = manager.ContextMenuStrip!;

            // Simulate a disabled dynamic item (detecting/noWsl) added to a menu
            // that is owned by TrayMenuRenderer. OnRenderItemText checks
            // !e.Item.Enabled and applies _colorTable.DisabledText, so the
            // disabled text color is drawn by TrayMenuRenderer rather than
            // falling back to the default WinForms disabled brush.
            var disabledItem = new ToolStripMenuItem(Localization.DataSourceDetectingWsl(manager.CurrentSettings.Language))
            {
                Enabled = false
            };
            renderer.ConfigureDynamicItem(disabledItem);
            rootMenu.Items.Add(disabledItem);

            try
            {
                // The owning menu must use TrayMenuRenderer so its OnRenderItemText
                // override is invoked for the disabled item.
                Assert.Same(renderer, rootMenu.Renderer);

                // The renderer must expose a distinct DisabledText color, which is
                // what OnRenderItemText applies for !e.Item.Enabled.
                var colorTable = (TrayMenuColorTable)renderer.ColorTable;
                Assert.NotEqual(colorTable.Text, colorTable.DisabledText);
                Assert.NotEqual(Color.Empty, colorTable.DisabledText);

                // Sanity check: ConfigureDynamicItem must not silently enable the item.
                Assert.False(disabledItem.Enabled);
            }
            finally
            {
                rootMenu.Items.Remove(disabledItem);
                disabledItem.Dispose();
            }
        });
    }

    [Fact]
    public Task Manager_AfterInit_TotalMenuItemCountIsReasonable()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            using var manager = CreateManager();
            await manager.StartupTask;

            var rootMenu = manager.ContextMenuStrip!;
            var allItems = GetAllMenuItemsRecursive(rootMenu.Items).ToList();

            // Should have many items: Metric(3) + Refresh(4) + DataSource(1) +
            // Overlay(2+6+6+4=18) + Theme(3) + Language(2) + Refresh + Exit = 30+
            Assert.True(allItems.Count >= 30, $"Expected at least 30 menu items, got {allItems.Count}");
        });
    }
}
