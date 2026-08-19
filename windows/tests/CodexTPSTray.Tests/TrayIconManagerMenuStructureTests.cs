using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CodexTPSCore;
using Xunit;

namespace CodexTPSTray.Tests;

[Collection("TrayIconManagerTests")]
public class TrayIconManagerMenuStructureTests
{
    private sealed class FakeSettingsStore : ITraySettingsStore
    {
        private readonly TraySettings _settings;

        public FakeSettingsStore(TraySettings settings) => _settings = settings;

        public TraySettings Load() => _settings;
        public bool TrySave(TraySettings settings) => true;
    }

    private static IEnumerable<ToolStripItem> GetAllMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem item in items)
        {
            yield return item;
            if (item is ToolStripMenuItem menuItem && menuItem.HasDropDownItems)
            {
                foreach (var child in GetAllMenuItems(menuItem.DropDownItems))
                    yield return child;
            }
        }
    }

    [Fact]
    public Task Menu_NoMetricsSubmenu()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                settingsStore,
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

            var allItemTexts = GetAllMenuItems(manager.ContextMenuStrip!.Items)
                .Select(i => i is ToolStripMenuItem mi ? mi.Text : "-")
                .ToList();

            Assert.DoesNotContain("Metrics", allItemTexts);
            Assert.DoesNotContain("指标", allItemTexts);
        });
    }

    [Fact]
    public Task Menu_MetricWindowSubmenuExists()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                settingsStore,
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

            var allItemTexts = GetAllMenuItems(manager.ContextMenuStrip!.Items)
                .Select(i => i is ToolStripMenuItem mi ? mi.Text : "-")
                .ToList();

            Assert.Contains("Metric Window", allItemTexts);
        });
    }

    [Fact]
    public Task Menu_KeyMenusStillPresent()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                settingsStore,
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

            var allItemTexts = GetAllMenuItems(manager.ContextMenuStrip!.Items)
                .Select(i => i is ToolStripMenuItem mi ? mi.Text : "-")
                .ToList();

            Assert.Contains("Metric Window", allItemTexts);
            Assert.Contains("Refresh Cadence", allItemTexts);
            Assert.Contains("Data Source", allItemTexts);
            Assert.Contains("Open Sessions Folder", allItemTexts);
            Assert.Contains("Launch at Login", allItemTexts);
            Assert.Contains("Overlay", allItemTexts);
            Assert.Contains("Theme", allItemTexts);
            Assert.Contains("Language", allItemTexts);
            Assert.Contains("Refresh", allItemTexts);
            Assert.Contains("Exit", allItemTexts);
        });
    }

    [Fact]
    public Task Menu_MetricWindowSubmenu_HasAllFourTimeWindows()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                settingsStore,
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

            var allItemTexts = GetAllMenuItems(manager.ContextMenuStrip!.Items)
                .Select(i => i is ToolStripMenuItem mi ? mi.Text : "-")
                .ToList();

            Assert.Contains("1 min", allItemTexts);
            Assert.Contains("5 min", allItemTexts);
            Assert.Contains("30 min", allItemTexts);
            Assert.Contains("1 hour", allItemTexts);
        });
    }

    [Fact]
    public Task Menu_OverlaySubmenu_HasMemorySubmenu()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                settingsStore,
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

            var allItemTexts = GetAllMenuItems(manager.ContextMenuStrip!.Items)
                .Select(i => i is ToolStripMenuItem mi ? mi.Text : "-")
                .ToList();

            Assert.Contains("Memory", allItemTexts);
            Assert.Contains("Appearance", allItemTexts);
            Assert.Contains("Shared across displays", allItemTexts);
            Assert.Contains("Remember per display", allItemTexts);
        });
    }

    [Fact]
    public Task Menu_OverlaySubmenu_MemorySubmenu_Chinese()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default with { Language = Language.Chinese };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                settingsStore,
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

            var allItemTexts = GetAllMenuItems(manager.ContextMenuStrip!.Items)
                .Select(i => i is ToolStripMenuItem mi ? mi.Text : "-")
                .ToList();

            Assert.Contains("记忆", allItemTexts);
            Assert.Contains("外观", allItemTexts);
            Assert.Contains("跨屏共用", allItemTexts);
            Assert.Contains("按显示器记忆", allItemTexts);
        });
    }

    [Fact]
    public Task Menu_OverlaySubmenu_PositionMemory_SharedCheckedByDefault()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = new TrayIconManager(
                settingsStore,
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

            var allItems = GetAllMenuItems(manager.ContextMenuStrip!.Items)
                .OfType<ToolStripMenuItem>()
                .ToList();

            var sharedItems = allItems.Where(i => i.Text == "Shared across displays").ToList();
            var rememberItems = allItems.Where(i => i.Text == "Remember per display").ToList();

            Assert.True(sharedItems.Count >= 2, "Should have at least two Shared-across-displays items (position + appearance)");
            Assert.True(rememberItems.Count >= 2, "Should have at least two Remember-per-display items (position + appearance)");
            Assert.All(sharedItems, item => Assert.True(item.Checked));
            Assert.All(rememberItems, item => Assert.False(item.Checked));
        });
    }
}
