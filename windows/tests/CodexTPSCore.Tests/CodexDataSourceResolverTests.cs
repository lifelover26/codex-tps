using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexTPSCore;
using Xunit;

namespace CodexTPSCore.Tests;

// Phase 2 tests for CodexDataSourceResolver. Verifies:
//   - Windows resolution is synchronous and never touches wsl.exe.
//   - WSL resolution targets exactly the selected distribution and never
//     runs `wsl --list --quiet`.
//   - WSL failures (non-zero exit, timeout, cancellation, illegal path,
//     missing sessions, wsl.exe absent) return null without throwing.
//   - The UNC path is re-resolved on every call (no caching).
//
// All tests use FakeWslProcessRunner / FakeCodexHomeDirectoryChecker: no real
// wsl.exe is spawned and none of these tests depend on WSL being installed.
public class CodexDataSourceResolverTests
{
    private const string ExpectedCodexHomeCommand =
        @"printf ""%s"" ""${CODEX_HOME:-$HOME/.codex}""";

    private static WslProcessResult DistOk(string linuxPath) =>
        new(ExitCode: 0, StandardOutput: linuxPath, StandardError: string.Empty);

    // A handler that only answers the per-distribution call for the named
    // distribution and throws if `--list --quiet` is ever invoked. Used to
    // prove the resolver never enumerates distributions.
    private static Func<IReadOnlyList<string>, Encoding, CancellationToken, WslProcessResult>
        HandlerForSingleDistribution(string distribution, string linuxPath) =>
        (args, _, _) =>
        {
            if (args.Count == 2 && args[0] == "--list" && args[1] == "--quiet")
            {
                throw new InvalidOperationException("resolver must not run --list");
            }
            if (args.Count == 6 && args[0] == "--distribution" && args[1] == distribution && args[2] == "--exec")
            {
                return DistOk(linuxPath);
            }
            return new WslProcessResult(ExitCode: 1, StandardOutput: "", StandardError: "unknown");
        };

    // ---------------------------------------------------------------------
    // Windows
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Resolve_Windows_DoesNotCallWslRunnerOrChecker()
    {
        var runner = FakeWslProcessRunner.FromSync((_, _, _) =>
            throw new InvalidOperationException("Windows resolution must not call wsl.exe"));
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(runner, checker));

        var resolved = await resolver.ResolveAsync(CodexDataSourceSelection.Windows, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(CodexDataSourceKind.Windows, resolved!.Selection.Kind);
        Assert.Equal("Windows", resolved.DisplayName);
        Assert.False(string.IsNullOrEmpty(resolved.CodexHome));
        Assert.Empty(runner.Calls);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task Resolve_Windows_UsesCodeHomeEnvVar()
    {
        // Proves the resolver returns SessionScanner.DefaultCodexHome() rather
        // than a hardcoded path. The env var is restored in finally so this
        // test cannot leak state to other test classes.
        string? original = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", @"C:\TestCodexHome");
            var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(
                FakeWslProcessRunner.FromSync((_, _, _) =>
                    throw new InvalidOperationException("should not be called")),
                new FakeCodexHomeDirectoryChecker(Array.Empty<string>())));

            var resolved = await resolver.ResolveAsync(CodexDataSourceSelection.Windows, CancellationToken.None);

            Assert.NotNull(resolved);
            Assert.Equal(@"C:\TestCodexHome", resolved!.CodexHome);
            Assert.Equal("Windows", resolved.DisplayName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", original);
        }
    }

    [Fact]
    public async Task Resolve_Windows_DoesNotRequireSessionsDirectoryToExist()
    {
        // Windows resolution must succeed even when no sessions dir exists; the
        // scanner surfaces that as SessionsDirectoryMissing, not a resolution
        // failure. The checker is never consulted.
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(
            FakeWslProcessRunner.FromSync((_, _, _) =>
                throw new InvalidOperationException("should not be called")),
            checker));

        var resolved = await resolver.ResolveAsync(CodexDataSourceSelection.Windows, CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Empty(checker.Checks);
    }

    // ---------------------------------------------------------------------
    // WSL single-source resolution
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Resolve_Wsl_CallsOnlyTargetDistributionAndNeverLists()
    {
        var expectedUnc = @"\\wsl.localhost\Ubuntu\home\u\.codex";
        var runner = FakeWslProcessRunner.FromSync(
            HandlerForSingleDistribution("Ubuntu", "/home/u/.codex"));
        var checker = new FakeCodexHomeDirectoryChecker(new[] { expectedUnc });
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(runner, checker));

        var resolved = await resolver.ResolveAsync(
            CodexDataSourceSelection.ForWsl("Ubuntu"), CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(CodexDataSourceKind.Wsl, resolved!.Selection.Kind);
        Assert.Equal("Ubuntu", resolved.Selection.WslDistributionName);
        Assert.Equal("WSL: Ubuntu", resolved.DisplayName);
        Assert.Equal(expectedUnc, resolved.CodexHome);

        // Exactly one wsl.exe call, targeting the selected distribution only.
        Assert.Single(runner.Calls);
        var call = runner.Calls[0];
        Assert.Equal(6, call.Arguments.Count);
        Assert.Equal("--distribution", call.Arguments[0]);
        Assert.Equal("Ubuntu", call.Arguments[1]);
        Assert.Equal("--exec", call.Arguments[2]);
        Assert.Equal("sh", call.Arguments[3]);
        Assert.Equal("-lc", call.Arguments[4]);
        Assert.Equal(ExpectedCodexHomeCommand, call.Arguments[5]);
    }

    [Fact]
    public async Task Resolve_Wsl_ReResolvesOnEveryCallWithoutCaching()
    {
        // Two calls return different Linux paths; the resolver must reflect the
        // current output rather than a cached value from the first call.
        var paths = new Queue<string>(new[] { "/home/first/.codex", "/home/second/.codex" });
        var runner = FakeWslProcessRunner.FromSync((args, _, _) =>
        {
            if (args.Count == 6 && args[0] == "--distribution")
            {
                return DistOk(paths.Dequeue());
            }
            return new WslProcessResult(1, "", "");
        });
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\Ubuntu\home\first\.codex",
            @"\\wsl.localhost\Ubuntu\home\second\.codex",
        });
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(runner, checker));
        var selection = CodexDataSourceSelection.ForWsl("Ubuntu");

        var first = await resolver.ResolveAsync(selection, CancellationToken.None);
        var second = await resolver.ResolveAsync(selection, CancellationToken.None);

        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\first\.codex", first!.CodexHome);
        Assert.Equal(@"\\wsl.localhost\Ubuntu\home\second\.codex", second!.CodexHome);
        Assert.Equal(2, runner.Calls.Count);
    }

    // ---------------------------------------------------------------------
    // WSL failure paths — each returns null without throwing
    // ---------------------------------------------------------------------

    [Fact]
    public async Task Resolve_Wsl_NonZeroExit_ReturnsNullWithoutThrowing()
    {
        var runner = FakeWslProcessRunner.FromSync((args, _, _) =>
        {
            if (args.Count == 6 && args[0] == "--distribution")
            {
                return new WslProcessResult(ExitCode: 1, StandardOutput: "", StandardError: "distro failed");
            }
            return new WslProcessResult(1, "", "");
        });
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(
            runner, new FakeCodexHomeDirectoryChecker(Array.Empty<string>())));

        var resolved = await resolver.ResolveAsync(
            CodexDataSourceSelection.ForWsl("Ubuntu"), CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task Resolve_Wsl_TimesOut_ReturnsNullWithoutThrowing()
    {
        // The discovery applies its own per-distribution timeout via a linked
        // CTS; the handler awaits the token so the timeout cancels it.
        var runner = new FakeWslProcessRunner(async (args, _, ct) =>
        {
            if (args.Count == 6 && args[0] == "--distribution")
            {
                await Task.Delay(TimeSpan.FromMinutes(1), ct);
                return DistOk("/home/u/.codex");
            }
            return new WslProcessResult(1, "", "");
        });
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(
            runner,
            new FakeCodexHomeDirectoryChecker(Array.Empty<string>()),
            perDistributionTimeout: TimeSpan.FromMilliseconds(50)));

        var resolved = await resolver.ResolveAsync(
            CodexDataSourceSelection.ForWsl("Ubuntu"), CancellationToken.None);

        Assert.Null(resolved);
    }

    [Fact]
    public async Task Resolve_Wsl_PreCancelledToken_ReturnsNullWithoutCallingWsl()
    {
        var runner = FakeWslProcessRunner.FromSync((_, _, _) =>
            throw new InvalidOperationException("should not be called"));
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(
            runner, new FakeCodexHomeDirectoryChecker(Array.Empty<string>())));

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var resolved = await resolver.ResolveAsync(
            CodexDataSourceSelection.ForWsl("Ubuntu"), cts.Token);

        Assert.Null(resolved);
        Assert.Empty(runner.Calls);
    }

    [Fact]
    public async Task Resolve_Wsl_RelativePathOutput_ReturnsNull()
    {
        var runner = FakeWslProcessRunner.FromSync(
            HandlerForSingleDistribution("Ubuntu", "relative/path"));
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(runner, checker));

        var resolved = await resolver.ResolveAsync(
            CodexDataSourceSelection.ForWsl("Ubuntu"), CancellationToken.None);

        Assert.Null(resolved);
        // The converter must reject the path before the checker is probed.
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task Resolve_Wsl_SessionsDirectoryMissing_ReturnsNull()
    {
        var runner = FakeWslProcessRunner.FromSync(
            HandlerForSingleDistribution("Ubuntu", "/home/u/.codex"));
        // Checker reports no sessions dir → the distribution is unavailable.
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(runner, checker));

        var resolved = await resolver.ResolveAsync(
            CodexDataSourceSelection.ForWsl("Ubuntu"), CancellationToken.None);

        Assert.Null(resolved);
        Assert.Contains(@"\\wsl.localhost\Ubuntu\home\u\.codex", checker.Checks);
    }

    [Fact]
    public async Task Resolve_Wsl_WslMissing_ReturnsNullWithoutThrowing()
    {
        // Simulate wsl.exe not installed: the per-distribution call throws
        // FileNotFoundException, which the discovery swallows.
        var runner = FakeWslProcessRunner.FromSync((args, _, _) =>
        {
            if (args.Count == 6 && args[0] == "--distribution")
            {
                throw new FileNotFoundException("wsl.exe not found", "wsl.exe");
            }
            throw new InvalidOperationException("unexpected call");
        });
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(
            runner, new FakeCodexHomeDirectoryChecker(Array.Empty<string>())));

        var resolved = await resolver.ResolveAsync(
            CodexDataSourceSelection.ForWsl("Ubuntu"), CancellationToken.None);

        Assert.Null(resolved);
    }

    // ---------------------------------------------------------------------
    // Constructor / argument validation
    // ---------------------------------------------------------------------

    [Fact]
    public void Constructor_RejectsNullDiscovery()
    {
        Assert.Throws<ArgumentNullException>(() => new CodexDataSourceResolver(null!));
    }

    [Fact]
    public async Task Resolve_NullSelection_ThrowsArgumentNullException()
    {
        var resolver = new CodexDataSourceResolver(new WslCodexHomeDiscovery(
            FakeWslProcessRunner.FromSync((_, _, _) => DistOk("")),
            new FakeCodexHomeDirectoryChecker(Array.Empty<string>())));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            resolver.ResolveAsync(null!, CancellationToken.None));
    }
}
