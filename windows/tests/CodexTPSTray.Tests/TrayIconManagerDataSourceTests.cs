using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodexTPSCore;
using CodexTPSTray;
using Xunit;

namespace CodexTPSTray.Tests;

[Collection("TrayIconManagerTests")]
public class TrayIconManagerDataSourceTests : IDisposable
{
    private readonly string _tempDirectory;

    public TrayIconManagerDataSourceTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"CodexTPSTest_{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    private sealed class FakeCodexDataSourceResolver : ICodexDataSourceResolver
    {
        public Func<CodexDataSourceSelection, CancellationToken, Task<ResolvedCodexDataSource?>>? ResolveFunc { get; set; }
        public int ResolveCallCount { get; private set; }
        public CodexDataSourceSelection? LastResolvedSelection { get; private set; }

        public Task<ResolvedCodexDataSource?> ResolveAsync(CodexDataSourceSelection selection, CancellationToken cancellationToken)
        {
            ResolveCallCount++;
            LastResolvedSelection = selection;
            return ResolveFunc?.Invoke(selection, cancellationToken) ?? Task.FromResult<ResolvedCodexDataSource?>(null);
        }
    }

    private sealed class FakeWslCodexHomeDiscovery : IWslCodexHomeDiscovery
    {
        public Func<CancellationToken, Task<IReadOnlyList<ResolvedCodexDataSource>>>? DiscoverFunc { get; set; }
        public int DiscoverCallCount { get; private set; }

        public async Task<IReadOnlyList<ResolvedCodexDataSource>> DiscoverAsync(CancellationToken cancellationToken)
        {
            DiscoverCallCount++;
            return DiscoverFunc?.Invoke(cancellationToken) is Task<IReadOnlyList<ResolvedCodexDataSource>> task
                ? await task.ConfigureAwait(false)
                : Array.Empty<ResolvedCodexDataSource>();
        }

        public Task<ResolvedCodexDataSource?> ResolveSingleDistributionAsync(string distributionName, CancellationToken cancellationToken)
        {
            return Task.FromResult<ResolvedCodexDataSource?>(null);
        }
    }

    private sealed class FakeSettingsStore : ITraySettingsStore
    {
        private TraySettings _settings;
        private readonly bool _saveShouldFail;
        public int SaveCallCount { get; private set; }

        public FakeSettingsStore(TraySettings initialSettings, bool saveShouldFail = false)
        {
            _settings = initialSettings;
            _saveShouldFail = saveShouldFail;
        }

        public TraySettings Load() => _settings;

        public bool TrySave(TraySettings settings)
        {
            SaveCallCount++;
            if (_saveShouldFail)
                return false;
            _settings = settings;
            return true;
        }
    }

    private static TrayIconManager CreateManager(
        TraySettings settings,
        ITraySettingsStore settingsStore,
        ICodexDataSourceResolver dataSourceResolver,
        IWslCodexHomeDiscovery wslDiscovery,
        SessionScanner? sessionScanner = null,
        IShellLauncher? shellLauncher = null)
    {
        return new TrayIconManager(
            settingsStore,
            settings,
            sessionScanner ?? new SessionScanner(),
            shellLauncher ?? new FakeShellLauncher(),
            new FakeRunKeyStore(),
            () => null,
            new WpfMonitorWorkAreaProvider(),
            new WindowsFormsThemeApplier(),
            new ThemeResolver(new WindowsSystemThemeSource()),
            dataSourceResolver,
            wslDiscovery
        );
    }

    private static async Task WaitForRefreshSemaphoreAsync(TrayIconManager manager, TimeSpan timeout)
    {
        var semaphore = manager.RefreshSemaphore;
        if (!await semaphore.WaitAsync(timeout))
            throw new TimeoutException("Timed out waiting for refresh semaphore");
        semaphore.Release();
    }

    private static async Task WaitForSwitchSemaphoreAsync(TrayIconManager manager, TimeSpan timeout)
    {
        var semaphore = manager.SwitchSemaphore;
        if (!await semaphore.WaitAsync(timeout))
            throw new TimeoutException("Timed out waiting for switch semaphore");
        semaphore.Release();
    }

    private static async Task WaitForDataSourceDiscoveryAsync(TrayIconManager manager, TimeSpan timeout)
    {
        var task = manager.DataSourceDiscoveryTask ?? Task.CompletedTask;
        await task.WaitAsync(timeout);
    }

    private static async Task WaitForLastSwitchTaskAsync(TrayIconManager manager, TimeSpan timeout)
    {
        var task = manager.LastDataSourceSwitchTask ?? Task.CompletedTask;
        await task.WaitAsync(timeout);
    }

    [Fact]
    public Task WindowsStartup_NoWslCallsAndTimerEnabled()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var fakeResolver = new FakeCodexDataSourceResolver();
            var fakeDiscovery = new FakeWslCodexHomeDiscovery();
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            Assert.Equal(0, fakeResolver.ResolveCallCount);
            Assert.Equal(0, fakeDiscovery.DiscoverCallCount);
            Assert.True(manager.IsRefreshTimerEnabled);
            Assert.True(manager.IsScannerReady);
        });
    }

    [Fact]
    public Task WslStartup_ResolvesOnlySpecifiedDistribution()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var wslSelection = CodexDataSourceSelection.ForWsl("Ubuntu");
            var settings = TraySettings.Default with { DataSource = wslSelection };
            var resolvedPath = Path.Combine(_tempDirectory, "wsl_codex");
            Directory.CreateDirectory(Path.Combine(resolvedPath, "sessions"));

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                    Task.FromResult<ResolvedCodexDataSource?>(new ResolvedCodexDataSource(selection, "WSL: Ubuntu", resolvedPath))
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery();
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            Assert.Equal(1, fakeResolver.ResolveCallCount);
            Assert.Equal("Ubuntu", fakeResolver.LastResolvedSelection?.WslDistributionName);
            Assert.Equal(0, fakeDiscovery.DiscoverCallCount);
            Assert.Equal(Path.Combine(resolvedPath, "sessions"), manager.SessionScanner.SessionsRoot);
            Assert.True(manager.IsScannerReady);
            Assert.True(manager.IsRefreshTimerEnabled);
        });
    }

    [Fact]
    public Task WslStartup_ResolutionFailure_TimerNotStartedAndSettingsUnchanged()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var wslSelection = CodexDataSourceSelection.ForWsl("Ubuntu");
            var settings = TraySettings.Default with { DataSource = wslSelection };
            var settingsStore = new FakeSettingsStore(settings);

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) => Task.FromResult<ResolvedCodexDataSource?>(null)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery();

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            Assert.False(manager.IsScannerReady);
            Assert.False(manager.IsRefreshTimerEnabled);
            Assert.Equal(CodexDataSourceKind.Wsl, manager.CurrentSettings.DataSource.Kind);
            Assert.Equal("Ubuntu", manager.CurrentSettings.DataSource.WslDistributionName);
        });
    }

    [Fact]
    public Task WslStartupFailure_RetrySuccess_StartsTimerAndRefreshes()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var wslSelection = CodexDataSourceSelection.ForWsl("Ubuntu");
            var settings = TraySettings.Default with { DataSource = wslSelection };
            var resolvedPath = Path.Combine(_tempDirectory, "wsl_retry");
            Directory.CreateDirectory(Path.Combine(resolvedPath, "sessions"));

            int resolveCount = 0;
            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                {
                    resolveCount++;
                    if (resolveCount == 1)
                        return Task.FromResult<ResolvedCodexDataSource?>(null);
                    return Task.FromResult<ResolvedCodexDataSource?>(new ResolvedCodexDataSource(selection, "WSL: Ubuntu", resolvedPath));
                }
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(Array.Empty<ResolvedCodexDataSource>())
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            Assert.False(manager.IsRefreshTimerEnabled);
            Assert.False(manager.IsScannerReady);

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Contains("Ubuntu", manager.WslDataSourceMenuItems.Keys);
            manager.RaiseWslDataSourceClicked("Ubuntu");
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            Assert.True(manager.IsScannerReady);
            Assert.True(manager.IsRefreshTimerEnabled);
            Assert.Equal(Path.Combine(resolvedPath, "sessions"), manager.SessionScanner.SessionsRoot);
            Assert.Equal(2, resolveCount);

            manager.RaiseRefreshTimerTick();
            await WaitForRefreshSemaphoreAsync(manager, TimeSpan.FromSeconds(2));

            Assert.True(manager.LatestSnapshot.HasValue);
        });
    }

    [Fact]
    public Task SwitchToWindows_Success_CreatesNewScannerAndSavesSettings()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var wslSelection = CodexDataSourceSelection.ForWsl("Ubuntu");
            var settings = TraySettings.Default with { DataSource = wslSelection };
            var windowsPath = Path.Combine(_tempDirectory, "windows_codex");
            Directory.CreateDirectory(Path.Combine(windowsPath, "sessions"));

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                {
                    if (selection.Kind == CodexDataSourceKind.Windows)
                    {
                        return Task.FromResult<ResolvedCodexDataSource?>(new ResolvedCodexDataSource(selection, "Windows", windowsPath));
                    }
                    return Task.FromResult<ResolvedCodexDataSource?>(null);
                }
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery();
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            var initialScanner = manager.SessionScanner;
            manager.RaiseWindowsDataSourceClicked();
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            Assert.NotSame(initialScanner, manager.SessionScanner);
            Assert.Equal(Path.Combine(windowsPath, "sessions"), manager.SessionScanner.SessionsRoot);
            Assert.Equal(CodexDataSourceKind.Windows, settingsStore.Load().DataSource.Kind);
            Assert.Equal(CodexDataSourceKind.Windows, manager.CurrentSettings.DataSource.Kind);
        });
    }

    [Fact]
    public Task SwitchToWsl_Failure_PreservesOriginalScannerAndSettings()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var initialScanner = new SessionScanner();
            var ubuntuPath = Path.Combine(_tempDirectory, "ubuntu_codex");
            Directory.CreateDirectory(Path.Combine(ubuntuPath, "sessions"));

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                {
                    if (selection.Kind == CodexDataSourceKind.Wsl)
                        return Task.FromResult<ResolvedCodexDataSource?>(null);
                    return Task.FromResult<ResolvedCodexDataSource?>(new ResolvedCodexDataSource(selection, "Windows", SessionScanner.DefaultCodexHome()));
                }
            };
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", ubuntuPath)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery, initialScanner);
            manager.Start();
            await manager.StartupTask;

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Contains("Ubuntu", manager.WslDataSourceMenuItems.Keys);
            manager.RaiseWslDataSourceClicked("Ubuntu");
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Equal(1, fakeResolver.ResolveCallCount);
            Assert.Equal("Ubuntu", fakeResolver.LastResolvedSelection?.WslDistributionName);
            Assert.Same(initialScanner, manager.SessionScanner);
            Assert.Equal(CodexDataSourceKind.Windows, manager.CurrentSettings.DataSource.Kind);
            Assert.Equal(CodexDataSourceKind.Windows, settingsStore.Load().DataSource.Kind);
        });
    }

    [Fact]
    public Task SwitchSaveFailure_PreservesOriginalState()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var initialScanner = new SessionScanner();
            var windowsPath = Path.Combine(_tempDirectory, "save_fail_codex");
            Directory.CreateDirectory(Path.Combine(windowsPath, "sessions"));

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                    Task.FromResult<ResolvedCodexDataSource?>(new ResolvedCodexDataSource(selection, "Windows", windowsPath))
            };
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", windowsPath)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings, saveShouldFail: true);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery, initialScanner);
            manager.Start();
            await manager.StartupTask;

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Contains("Ubuntu", manager.WslDataSourceMenuItems.Keys);
            manager.RaiseWslDataSourceClicked("Ubuntu");
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Equal(1, fakeResolver.ResolveCallCount);
            Assert.Equal("Ubuntu", fakeResolver.LastResolvedSelection?.WslDistributionName);
            Assert.Equal(1, settingsStore.SaveCallCount);
            Assert.Same(initialScanner, manager.SessionScanner);
            Assert.Equal(CodexDataSourceKind.Windows, manager.CurrentSettings.DataSource.Kind);
        });
    }

    [Fact]
    public Task SessionFolderLauncher_FollowsActiveScanner()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var fakeShellLauncher = new FakeShellLauncher();
            var windowsPath = Path.Combine(_tempDirectory, "windows_sessions");
            var wslPath = Path.Combine(_tempDirectory, "wsl_sessions");
            Directory.CreateDirectory(Path.Combine(windowsPath, "sessions"));
            Directory.CreateDirectory(Path.Combine(wslPath, "sessions"));

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                {
                    var path = selection.Kind == CodexDataSourceKind.Wsl ? wslPath : windowsPath;
                    return Task.FromResult<ResolvedCodexDataSource?>(new ResolvedCodexDataSource(selection, "Windows", path));
                }
            };
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", wslPath)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery, sessionScanner: new SessionScanner(windowsPath), shellLauncher: fakeShellLauncher);
            manager.Start();
            await manager.StartupTask;

            manager.RaiseOpenSessionsFolderClicked();
            Assert.Single(fakeShellLauncher.LaunchCalls);
            Assert.Equal(Path.Combine(windowsPath, "sessions"), fakeShellLauncher.LaunchCalls[0]);

            fakeShellLauncher.LaunchCalls.Clear();

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));
            manager.RaiseWslDataSourceClicked("Ubuntu");
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            manager.RaiseOpenSessionsFolderClicked();
            Assert.Single(fakeShellLauncher.LaunchCalls);
            Assert.Equal(Path.Combine(wslPath, "sessions"), fakeShellLauncher.LaunchCalls[0]);
        });
    }

    [Fact]
    public Task UnavailableWslMenuItem_ClickableForRetry()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var wslSelection = CodexDataSourceSelection.ForWsl("Ubuntu");
            var settings = TraySettings.Default with { DataSource = wslSelection };
            int retryCount = 0;

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                {
                    if (selection.Kind == CodexDataSourceKind.Wsl && selection.WslDistributionName == "Ubuntu")
                    {
                        retryCount++;
                        return Task.FromResult<ResolvedCodexDataSource?>(null);
                    }
                    return Task.FromResult<ResolvedCodexDataSource?>(null);
                }
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(Array.Empty<ResolvedCodexDataSource>())
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Contains("Ubuntu", manager.WslDataSourceMenuItems.Keys);
            var ubuntuItem = manager.WslDataSourceMenuItems["Ubuntu"];
            Assert.True(ubuntuItem.Enabled);

            ubuntuItem.PerformClick();
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Equal(2, retryCount);
        });
    }

    [Fact]
    public Task ConcurrentSwitchAttempts_PreventedBySemaphore()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var windowsPath = Path.Combine(_tempDirectory, "windows_concurrent");
            var wslPath = Path.Combine(_tempDirectory, "wsl_concurrent");
            Directory.CreateDirectory(Path.Combine(windowsPath, "sessions"));
            Directory.CreateDirectory(Path.Combine(wslPath, "sessions"));

            int resolveCount = 0;
            var resolveStarted = new TaskCompletionSource();
            var gate = new TaskCompletionSource();
            var rejected = new TaskCompletionSource();

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = async (selection, ct) =>
                {
                    Interlocked.Increment(ref resolveCount);
                    if (selection.Kind == CodexDataSourceKind.Wsl)
                    {
                        resolveStarted.TrySetResult();
                        await gate.Task;
                    }
                    var path = selection.Kind == CodexDataSourceKind.Wsl ? wslPath : windowsPath;
                    return new ResolvedCodexDataSource(selection, selection.Kind == CodexDataSourceKind.Wsl ? "WSL: Ubuntu" : "Windows", path);
                }
            };
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", wslPath)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.OnSwitchRejected = () => rejected.TrySetResult();
            manager.Start();
            await manager.StartupTask;

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            manager.RaiseWslDataSourceClicked("Ubuntu");
            var firstSwitch = manager.LastDataSourceSwitchTask!;
            await resolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            manager.RaiseWslDataSourceClicked("Ubuntu");
            var secondSwitch = manager.LastDataSourceSwitchTask!;
            await rejected.Task.WaitAsync(TimeSpan.FromSeconds(2));

            gate.SetResult();
            await secondSwitch.WaitAsync(TimeSpan.FromSeconds(2));
            await firstSwitch.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, resolveCount);
            Assert.Equal(CodexDataSourceKind.Wsl, manager.CurrentSettings.DataSource.Kind);
        });
    }

    [Fact]
    public Task OldRefreshBlocked_SwitchWaitsUntilCompletion()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var refreshStarted = new TaskCompletionSource();
            var refreshBlocker = new TaskCompletionSource<UsageSnapshot>();
            var windowsPath = Path.Combine(_tempDirectory, "windows_blocked");
            var wslPath = Path.Combine(_tempDirectory, "wsl_blocked");
            Directory.CreateDirectory(Path.Combine(windowsPath, "sessions"));
            Directory.CreateDirectory(Path.Combine(wslPath, "sessions"));

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                {
                    var path = selection.Kind == CodexDataSourceKind.Wsl ? wslPath : windowsPath;
                    return Task.FromResult<ResolvedCodexDataSource?>(new ResolvedCodexDataSource(selection, selection.Kind == CodexDataSourceKind.Wsl ? "WSL: Ubuntu" : "Windows", path));
                }
            };
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", wslPath)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;
            await WaitForRefreshSemaphoreAsync(manager, TimeSpan.FromSeconds(2));

            var fakeScanner = new BlockingSessionScanner(() =>
            {
                refreshStarted.SetResult();
                return refreshBlocker.Task;
            });
            manager.SetSessionScanner(fakeScanner);

            var switchWaiting = new TaskCompletionSource();
            manager.OnSwitchAcquiringRefreshSemaphore = () => switchWaiting.TrySetResult();

            var refreshTask = Task.Run(() => manager.RaiseRefreshTimerTick());
            await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));
            manager.RaiseWslDataSourceClicked("Ubuntu");
            var switchTask = manager.LastDataSourceSwitchTask!;

            await switchWaiting.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(0, manager.RefreshSemaphore.CurrentCount);
            Assert.Equal(0, manager.SwitchSemaphore.CurrentCount);

            refreshBlocker.SetResult(UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.ReadFailed));
            await refreshTask.WaitAsync(TimeSpan.FromSeconds(2));
            await switchTask.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitForRefreshSemaphoreAsync(manager, TimeSpan.FromSeconds(2));
            await WaitForSwitchSemaphoreAsync(manager, TimeSpan.FromSeconds(2));

            Assert.NotSame(fakeScanner, manager.SessionScanner);
            Assert.Equal(Path.Combine(wslPath, "sessions"), manager.SessionScanner.SessionsRoot);
            Assert.Equal(CodexDataSourceKind.Wsl, manager.CurrentSettings.DataSource.Kind);
        });
    }

    [Fact]
    public Task ClickCurrentSelectionWhenReady_RestoresCheckmark()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, new FakeCodexDataSourceResolver(), new FakeWslCodexHomeDiscovery());
            manager.Start();
            await manager.StartupTask;

            manager.WindowsDataSourceMenuItem.Checked = false;

            manager.RaiseWindowsDataSourceClicked();

            Assert.True(manager.WindowsDataSourceMenuItemChecked);
            Assert.Equal(CodexDataSourceKind.Windows, manager.CurrentSettings.DataSource.Kind);
        });
    }

    [Fact]
    public Task SubmenuOpening_MultipleTimes_NoDuplicateItems()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", Path.Combine(_tempDirectory, "ubuntu"))
            };

            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, new FakeCodexDataSourceResolver(), fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));
            Assert.Single(manager.WslDataSourceMenuItems);

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Single(manager.WslDataSourceMenuItems);
            Assert.Equal(2, fakeDiscovery.DiscoverCallCount);
        });
    }

    [Fact]
    public Task Shutdown_CancelsOngoingRefreshWithoutExceptions()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var refreshBlocker = new TaskCompletionSource<UsageSnapshot>();
            var refreshStarted = new TaskCompletionSource();
            var fakeScanner = new BlockingSessionScanner(() =>
            {
                refreshStarted.SetResult();
                return refreshBlocker.Task;
            });
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, new FakeCodexDataSourceResolver(), new FakeWslCodexHomeDiscovery(), fakeScanner);
            manager.Start();
            await manager.StartupTask;

            var refreshTask = Task.Run(() => manager.RaiseRefreshTimerTick());
            await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            manager.Dispose();
            refreshBlocker.SetResult(UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.ReadFailed));

            await refreshTask.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(manager.IsShuttingDown);
        });
    }

    [Fact]
    public Task WslStartupRace_OldSelectionDoesNotOverrideNew()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var wslSelection = CodexDataSourceSelection.ForWsl("Ubuntu");
            var settings = TraySettings.Default with { DataSource = wslSelection };
            var windowsPath = Path.Combine(_tempDirectory, "race_windows");
            var wslPath = Path.Combine(_tempDirectory, "race_wsl");
            Directory.CreateDirectory(Path.Combine(windowsPath, "sessions"));
            Directory.CreateDirectory(Path.Combine(wslPath, "sessions"));

            var startupGate = new TaskCompletionSource();
            var startupResolveStarted = new TaskCompletionSource();
            int wslResolveCount = 0;

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = async (selection, ct) =>
                {
                    if (selection.Kind == CodexDataSourceKind.Wsl)
                    {
                        wslResolveCount++;
                        startupResolveStarted.SetResult();
                        await startupGate.Task;
                        return new ResolvedCodexDataSource(selection, "WSL: Ubuntu", wslPath);
                    }
                    return new ResolvedCodexDataSource(selection, "Windows", windowsPath);
                }
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, new FakeWslCodexHomeDiscovery());
            manager.Start();

            await startupResolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            manager.RaiseWindowsDataSourceClicked();
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Equal(CodexDataSourceKind.Windows, manager.CurrentSettings.DataSource.Kind);
            Assert.Equal(Path.Combine(windowsPath, "sessions"), manager.SessionScanner.SessionsRoot);

            startupGate.SetResult();
            await manager.StartupTask;

            Assert.Equal(1, wslResolveCount);
            Assert.Equal(CodexDataSourceKind.Windows, manager.CurrentSettings.DataSource.Kind);
            Assert.Equal(Path.Combine(windowsPath, "sessions"), manager.SessionScanner.SessionsRoot);
            Assert.True(manager.IsScannerReady);
            Assert.True(manager.IsRefreshTimerEnabled);
        });
    }

    [Fact]
    public Task ConcurrentSwitchIsRejectedWithoutFault()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var wslPath = Path.Combine(_tempDirectory, "reject_wsl");
            Directory.CreateDirectory(Path.Combine(wslPath, "sessions"));

            var firstSwitchGate = new TaskCompletionSource();
            var firstSwitchAcquired = new TaskCompletionSource();

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = async (selection, ct) =>
                {
                    if (selection.Kind == CodexDataSourceKind.Wsl)
                    {
                        firstSwitchAcquired.SetResult();
                        await firstSwitchGate.Task;
                        return new ResolvedCodexDataSource(selection, "WSL: Ubuntu", wslPath);
                    }
                    return new ResolvedCodexDataSource(selection, "Windows", SessionScanner.DefaultCodexHome());
                }
            };
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", wslPath)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            manager.RaiseWslDataSourceClicked("Ubuntu");
            var firstSwitch = manager.LastDataSourceSwitchTask!;
            await firstSwitchAcquired.Task.WaitAsync(TimeSpan.FromSeconds(2));

            manager.RaiseWslDataSourceClicked("Ubuntu");
            var secondSwitch = manager.LastDataSourceSwitchTask!;
            await secondSwitch.WaitAsync(TimeSpan.FromSeconds(2));

            firstSwitchGate.SetResult();
            await firstSwitch.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(1, manager.SwitchSemaphore.CurrentCount);
            Assert.Equal(CodexDataSourceKind.Wsl, manager.CurrentSettings.DataSource.Kind);
        });
    }

    [Fact]
    public Task WslDiscoveryFailure_ClearsStaleSourcesAndKeepsSavedAsRetry()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var ubuntuPath = Path.Combine(_tempDirectory, "ubuntu_fail");
            var debianPath = Path.Combine(_tempDirectory, "debian_fail");
            Directory.CreateDirectory(Path.Combine(ubuntuPath, "sessions"));
            Directory.CreateDirectory(Path.Combine(debianPath, "sessions"));

            int discoverCount = 0;
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct =>
                {
                    discoverCount++;
                    if (discoverCount == 1)
                    {
                        return Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(new[]
                        {
                            new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", ubuntuPath),
                            new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Debian"), "WSL: Debian", debianPath)
                        });
                    }
                    throw new InvalidOperationException("Discovery failed");
                }
            };
            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                {
                    var path = selection.WslDistributionName == "Ubuntu" ? ubuntuPath : debianPath;
                    return Task.FromResult<ResolvedCodexDataSource?>(new ResolvedCodexDataSource(selection, $"WSL: {selection.WslDistributionName}", path));
                }
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Equal(2, manager.WslDataSourceMenuItems.Count);
            Assert.Contains("Ubuntu", manager.WslDataSourceMenuItems.Keys);
            Assert.Contains("Debian", manager.WslDataSourceMenuItems.Keys);

            manager.RaiseWslDataSourceClicked("Ubuntu");
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Equal(CodexDataSourceKind.Wsl, manager.CurrentSettings.DataSource.Kind);
            Assert.Equal("Ubuntu", manager.CurrentSettings.DataSource.WslDistributionName);

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.Single(manager.WslDataSourceMenuItems);
            Assert.Contains("Ubuntu", manager.WslDataSourceMenuItems.Keys);
            Assert.DoesNotContain("Debian", manager.WslDataSourceMenuItems.Keys);
        });
    }

    [Fact]
    public Task SlowWslSwitch_NonDataSourceSettingChange_PreservedInFinalSettings()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var wslPath = Path.Combine(_tempDirectory, "concurrent_wsl");
            Directory.CreateDirectory(Path.Combine(wslPath, "sessions"));

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = (selection, ct) =>
                {
                    if (selection.Kind == CodexDataSourceKind.Wsl)
                    {
                        return Task.FromResult<ResolvedCodexDataSource?>(
                            new ResolvedCodexDataSource(selection, "WSL: Ubuntu", wslPath));
                    }
                    return Task.FromResult<ResolvedCodexDataSource?>(
                        new ResolvedCodexDataSource(selection, "Windows", SessionScanner.DefaultCodexHome()));
                }
            };
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", wslPath)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;
            await WaitForRefreshSemaphoreAsync(manager, TimeSpan.FromSeconds(2));

            var refreshStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var refreshBlocker = new TaskCompletionSource<UsageSnapshot>();
            var blockingScanner = new BlockingSessionScanner(() =>
            {
                refreshStarted.TrySetResult();
                return refreshBlocker.Task;
            });
            manager.SetSessionScanner(blockingScanner);

            var switchAcquiringSemaphore = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            manager.OnSwitchAcquiringRefreshSemaphore = () => switchAcquiringSemaphore.TrySetResult();

            var refreshTask = Task.Run(() => manager.RaiseRefreshTimerTick());
            await refreshStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            manager.RaiseWslDataSourceClicked("Ubuntu");
            var switchTask = manager.LastDataSourceSwitchTask!;
            await switchAcquiringSemaphore.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.Equal(0, manager.RefreshSemaphore.CurrentCount);
            Assert.True(manager.IsSwitchingDataSource);

            manager.RaiseLanguageSelected(Language.Chinese);
            Assert.Equal(Language.Chinese, manager.CurrentSettings.Language);

            refreshBlocker.SetResult(UsageSnapshot.Empty(DateTimeOffset.UtcNow, CollectionStatus.Ready));
            await refreshTask.WaitAsync(TimeSpan.FromSeconds(2));
            await switchTask.WaitAsync(TimeSpan.FromSeconds(2));
            await WaitForRefreshSemaphoreAsync(manager, TimeSpan.FromSeconds(2));
            await WaitForSwitchSemaphoreAsync(manager, TimeSpan.FromSeconds(2));

            Assert.False(manager.IsSwitchingDataSource);
            Assert.Equal(CodexDataSourceKind.Wsl, manager.CurrentSettings.DataSource.Kind);
            Assert.Equal("Ubuntu", manager.CurrentSettings.DataSource.WslDistributionName);
            Assert.Equal(Language.Chinese, manager.CurrentSettings.Language);
            Assert.Equal(Language.Chinese, settingsStore.Load().Language);
            Assert.Equal(CodexDataSourceKind.Wsl, settingsStore.Load().DataSource.Kind);
        });
    }

    [Fact]
    public Task SwitchInProgress_MenuRediscovery_NewWslItemsStayDisabledUntilComplete()
    {
        return WpfTestHelpers.RunInStaWithWpfAsync(async () =>
        {
            var settings = TraySettings.Default;
            var wslPath = Path.Combine(_tempDirectory, "rediscover_wsl");
            Directory.CreateDirectory(Path.Combine(wslPath, "sessions"));

            var resolveGate = new TaskCompletionSource<ResolvedCodexDataSource?>();
            var resolveStarted = new TaskCompletionSource();

            var fakeResolver = new FakeCodexDataSourceResolver
            {
                ResolveFunc = async (selection, ct) =>
                {
                    if (selection.Kind == CodexDataSourceKind.Wsl)
                    {
                        resolveStarted.TrySetResult();
                        return await resolveGate.Task.WaitAsync(ct);
                    }
                    return new ResolvedCodexDataSource(selection, "Windows", SessionScanner.DefaultCodexHome());
                }
            };
            var discoveredSources = new List<ResolvedCodexDataSource>
            {
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", wslPath),
                new ResolvedCodexDataSource(CodexDataSourceSelection.ForWsl("Debian"), "WSL: Debian", wslPath)
            };
            var fakeDiscovery = new FakeWslCodexHomeDiscovery
            {
                DiscoverFunc = ct => Task.FromResult<IReadOnlyList<ResolvedCodexDataSource>>(discoveredSources)
            };
            var settingsStore = new FakeSettingsStore(settings);

            using var manager = CreateManager(settings, settingsStore, fakeResolver, fakeDiscovery);
            manager.Start();
            await manager.StartupTask;

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.True(manager.WindowsDataSourceMenuItem.Enabled);
            foreach (var kvp in manager.WslDataSourceMenuItems)
                Assert.True(kvp.Value.Enabled);

            manager.RaiseWslDataSourceClicked("Ubuntu");
            await resolveStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(manager.IsSwitchingDataSource);
            Assert.False(manager.WindowsDataSourceMenuItem.Enabled);
            foreach (var kvp in manager.WslDataSourceMenuItems)
                Assert.False(kvp.Value.Enabled);

            manager.RaiseDataSourceSubmenuOpening();
            await WaitForDataSourceDiscoveryAsync(manager, TimeSpan.FromSeconds(2));

            Assert.True(manager.IsSwitchingDataSource);
            Assert.False(manager.WindowsDataSourceMenuItem.Enabled);
            Assert.True(manager.WslDataSourceMenuItems.ContainsKey("Ubuntu"));
            Assert.True(manager.WslDataSourceMenuItems.ContainsKey("Debian"));
            foreach (var kvp in manager.WslDataSourceMenuItems)
                Assert.False(kvp.Value.Enabled);

            resolveGate.SetResult(new ResolvedCodexDataSource(
                CodexDataSourceSelection.ForWsl("Ubuntu"), "WSL: Ubuntu", wslPath));
            await WaitForLastSwitchTaskAsync(manager, TimeSpan.FromSeconds(2));

            Assert.False(manager.IsSwitchingDataSource);
            Assert.True(manager.WindowsDataSourceMenuItem.Enabled);
            foreach (var kvp in manager.WslDataSourceMenuItems)
                Assert.True(kvp.Value.Enabled);
            Assert.Equal(CodexDataSourceKind.Wsl, manager.CurrentSettings.DataSource.Kind);
            Assert.Equal("Ubuntu", manager.CurrentSettings.DataSource.WslDistributionName);
        });
    }

    private sealed class BlockingSessionScanner : SessionScanner
    {
        private readonly Func<Task<UsageSnapshot>> _blockFunc;

        public BlockingSessionScanner(Func<Task<UsageSnapshot>> blockFunc)
            : base(codexHome: null)
        {
            _blockFunc = blockFunc;
        }

        public override UsageSnapshot Refresh(DateTimeOffset timestamp)
        {
            return _blockFunc().GetAwaiter().GetResult();
        }
    }
}