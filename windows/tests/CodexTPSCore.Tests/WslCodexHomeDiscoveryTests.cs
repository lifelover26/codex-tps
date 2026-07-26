using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexTPSCore;
using Xunit;

namespace CodexTPSCore.Tests;

public class WslCodexHomeDiscoveryTests
{
    // The exact per-distribution command the discovery must issue. Verified
    // verbatim in multiple tests so any drift in the shell invocation surfaces
    // immediately.
    private const string ExpectedCodexHomeCommand =
        @"printf ""%s"" ""${CODEX_HOME:-$HOME/.codex}""";

    private static WslProcessResult ListOk(string stdout) =>
        new(ExitCode: 0, StandardOutput: stdout, StandardError: string.Empty);

    private static WslProcessResult DistOk(string linuxPath) =>
        new(ExitCode: 0, StandardOutput: linuxPath, StandardError: string.Empty);

    // Builds a handler that returns the list output for `--list --quiet` and a
    // per-distribution linux path for `--distribution <name> --exec ...`.
    private static Func<IReadOnlyList<string>, Encoding, CancellationToken, WslProcessResult>
        HandlerForListAndPaths(string listOutput, IReadOnlyDictionary<string, string> pathsByDist)
    {
        return (args, _, _) =>
        {
            if (args.Count == 2 && args[0] == "--list" && args[1] == "--quiet")
            {
                return ListOk(listOutput);
            }
            if (args.Count == 6 && args[0] == "--distribution" && args[2] == "--exec")
            {
                var name = args[1];
                if (pathsByDist.TryGetValue(name, out var path))
                {
                    return DistOk(path);
                }
            }
            return new WslProcessResult(ExitCode: 1, StandardOutput: string.Empty, StandardError: "unknown");
        };
    }

    [Fact]
    public async Task DiscoversMultipleDistributionsAndSortsThem()
    {
        var listOutput = "Ubuntu\nDebian\nkali-linux\n";
        var paths = new Dictionary<string, string>
        {
            ["Ubuntu"] = "/home/test-user/.codex",
            ["Debian"] = "/home/debian-user/.codex",
            ["kali-linux"] = "/home/kali-user/.codex",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        // All three have sessions/ available.
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\Ubuntu\home\test-user\.codex",
            @"\\wsl.localhost\Debian\home\debian-user\.codex",
            @"\\wsl.localhost\kali-linux\home\kali-user\.codex",
        });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(new[] { "Debian", "Ubuntu", "kali-linux" },
            result.Select(r => r.Selection.WslDistributionName).ToArray());
        Assert.All(result, r => Assert.Equal(CodexDataSourceKind.Wsl, r.Selection.Kind));
    }

    [Fact]
    public async Task DeduplicatesDistributionsFromNoisyListOutput()
    {
        // CR, NUL, blank lines, duplicates — all must collapse to a clean set.
        var listOutput = "Ubuntu\r\nU\0b\0u\0n\0t\0u\0\r\0\n\0Debian\n\nDebian\n";
        var paths = new Dictionary<string, string>
        {
            ["Ubuntu"] = "/home/u/.codex",
            ["Debian"] = "/home/d/.codex",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\Ubuntu\home\u\.codex",
            @"\\wsl.localhost\Debian\home\d\.codex",
        });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(new[] { "Debian", "Ubuntu" },
            result.Select(r => r.Selection.WslDistributionName).ToArray());
    }

    [Fact]
    public async Task DistributionNameWithSpacesStaysSingleArgument()
    {
        var listOutput = "My Distro\n";
        var paths = new Dictionary<string, string>
        {
            ["My Distro"] = "/home/user/.codex",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\My Distro\home\user\.codex",
        });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("My Distro", result[0].Selection.WslDistributionName);

        // The per-distribution call must pass the name as a single argv element
        // (args[1]) — never split on spaces. The full argument list must be
        // exactly the 6 entries the discovery assembles.
        var distCall = runner.Calls.Single(c => c.Arguments.Count == 6);
        Assert.Equal("My Distro", distCall.Arguments[1]);
        Assert.Equal("--distribution", distCall.Arguments[0]);
        Assert.Equal("--exec", distCall.Arguments[2]);
        Assert.Equal("sh", distCall.Arguments[3]);
        Assert.Equal("-lc", distCall.Arguments[4]);
        Assert.Equal(ExpectedCodexHomeCommand, distCall.Arguments[5]);
    }

    [Fact]
    public async Task ResolvesDefaultHomeCodexWhenCodeEnvVarUnset()
    {
        // sh -lc falls back to $HOME/.codex when CODEX_HOME is unset/empty.
        var listOutput = "Ubuntu\n";
        var paths = new Dictionary<string, string>
        {
            ["Ubuntu"] = "/home/test-user/.codex",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var expectedUnc = @"\\wsl.localhost\Ubuntu\home\test-user\.codex";
        var checker = new FakeCodexHomeDirectoryChecker(new[] { expectedUnc });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        var resolved = Assert.Single(result);
        Assert.Equal(expectedUnc, resolved.CodexHome);
        Assert.Equal("WSL: Ubuntu", resolved.DisplayName);
    }

    [Fact]
    public async Task ResolvesExplicitCodeEnvVarAbsolutePath()
    {
        var listOutput = "Debian\n";
        var paths = new Dictionary<string, string>
        {
            ["Debian"] = "/srv/codex/custom-home",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var expectedUnc = @"\\wsl.localhost\Debian\srv\codex\custom-home";
        var checker = new FakeCodexHomeDirectoryChecker(new[] { expectedUnc });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        var resolved = Assert.Single(result);
        Assert.Equal(expectedUnc, resolved.CodexHome);
        Assert.Equal("WSL: Debian", resolved.DisplayName);
    }

    [Fact]
    public async Task RequestsUtf16ForListAndUtf8ForPerDistributionCalls()
    {
        var listOutput = "Ubuntu\n";
        var paths = new Dictionary<string, string> { ["Ubuntu"] = "/home/u/.codex" };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\Ubuntu\home\u\.codex",
        });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        await discovery.DiscoverAsync(CancellationToken.None);

        var listCall = runner.Calls.Single(c => c.Arguments.Count == 2);
        Assert.Equal(Encoding.Unicode, listCall.Encoding);

        var distCall = runner.Calls.Single(c => c.Arguments.Count == 6);
        Assert.Equal(Encoding.UTF8, distCall.Encoding);
    }

    [Fact]
    public async Task SkipsDistributionWithInvalidName()
    {
        // ".." appears in the list but must be rejected before any wsl call.
        var listOutput = "Ubuntu\n..\n";
        var paths = new Dictionary<string, string>
        {
            ["Ubuntu"] = "/home/u/.codex",
            [".."] = "/should/never/be/called",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\Ubuntu\home\u\.codex",
        });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Ubuntu", result[0].Selection.WslDistributionName);
        // The invalid name must not have been passed to a per-distribution call.
        Assert.DoesNotContain(runner.Calls, c => c.Arguments.Count == 6 && c.Arguments[1] == "..");
    }

    [Fact]
    public async Task SkipsDistributionReturningRelativePath()
    {
        var listOutput = "Ubuntu\n";
        var paths = new Dictionary<string, string> { ["Ubuntu"] = "relative/path" };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
        // Directory checker must not be called for an invalid path.
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task SkipsDistributionReturningTraversalPath()
    {
        var listOutput = "Ubuntu\n";
        var paths = new Dictionary<string, string> { ["Ubuntu"] = "/home/../etc/passwd" };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task SkipsDistributionReturningBackslashTraversalPath()
    {
        // A Linux path containing a backslash would otherwise escape the
        // intended subtree after UNC conversion (the backslash becomes a
        // path separator). The converter must reject it before the directory
        // checker is ever probed with the malformed UNC.
        var listOutput = "Ubuntu\n";
        var paths = new Dictionary<string, string>
        {
            ["Ubuntu"] = "/home/user\\..\\etc/.codex",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task SkipsDistributionReturningPathWithBackslashInSegment()
    {
        // Even a "neutral" backslash that does not attempt traversal must be
        // rejected, because after conversion it would inject an extra UNC
        // component and probe a path the user did not authorize.
        var listOutput = "Ubuntu\n";
        var paths = new Dictionary<string, string>
        {
            ["Ubuntu"] = "/home/user\\bar/.codex",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task SkipsDistributionReturningEmptyOutput()
    {
        var listOutput = "Ubuntu\n";
        var paths = new Dictionary<string, string> { ["Ubuntu"] = "" };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task SingleDistributionFailureDoesNotAffectOthers()
    {
        var listOutput = "Ubuntu\nDebian\nkali-linux\n";
        // Debian returns non-zero; Ubuntu and kali succeed.
        var handler = (Func<IReadOnlyList<string>, Encoding, CancellationToken, WslProcessResult>)((args, _, _) =>
        {
            if (args.Count == 2 && args[0] == "--list" && args[1] == "--quiet")
                return ListOk(listOutput);
            if (args.Count == 6 && args[0] == "--distribution")
            {
                return args[1] switch
                {
                    "Ubuntu" => DistOk("/home/u/.codex"),
                    "Debian" => new WslProcessResult(1, "", "distro failed to start"),
                    "kali-linux" => DistOk("/home/k/.codex"),
                    _ => new WslProcessResult(1, "", "unknown"),
                };
            }
            return new WslProcessResult(1, "", "unknown");
        });
        var runner = FakeWslProcessRunner.FromSync(handler);
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\Ubuntu\home\u\.codex",
            @"\\wsl.localhost\kali-linux\home\k\.codex",
        });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Equal(new[] { "Ubuntu", "kali-linux" },
            result.Select(r => r.Selection.WslDistributionName).ToArray());
    }

    [Fact]
    public async Task WslMissingReturnsEmptyListWithoutThrowing()
    {
        // Simulate wsl.exe not installed: the list call throws FileNotFoundException.
        var runner = FakeWslProcessRunner.FromSync((_, _, _) =>
        {
            throw new FileNotFoundException("wsl.exe not found", "wsl.exe");
        });
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task WslListNonZeroExitReturnsEmptyListWithoutThrowing()
    {
        var runner = FakeWslProcessRunner.FromSync((args, _, _) =>
        {
            if (args.Count == 2 && args[0] == "--list" && args[1] == "--quiet")
                return new WslProcessResult(ExitCode: -1, StandardOutput: "", StandardError: "wsl error");
            return new WslProcessResult(1, "", "");
        });
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task WslListTimeoutReturnsEmptyListWithoutThrowing()
    {
        // The discovery enforces its own list timeout via a linked CTS. The
        // handler awaits the token; when the short timeout fires, Task.Delay
        // throws OperationCanceledException, which the discovery swallows.
        var runner = new FakeWslProcessRunner(async (_, _, ct) =>
        {
            await Task.Delay(TimeSpan.FromMinutes(1), ct);
            return ListOk("Ubuntu\n");
        });
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(
            runner, checker,
            listTimeout: TimeSpan.FromMilliseconds(50),
            perDistributionTimeout: TimeSpan.FromSeconds(5));

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task PerDistributionTimeoutSkipsOnlyThatDistribution()
    {
        var listOutput = "Ubuntu\nDebian\n";
        var runner = new FakeWslProcessRunner(async (args, _, ct) =>
        {
            if (args.Count == 2 && args[0] == "--list" && args[1] == "--quiet")
                return ListOk(listOutput);
            if (args.Count == 6 && args[0] == "--distribution")
            {
                if (args[1] == "Debian")
                {
                    // Simulate a hung distribution: never completes before the
                    // per-distribution timeout cancels the token.
                    await Task.Delay(TimeSpan.FromMinutes(1), ct);
                }
                return DistOk("/home/u/.codex");
            }
            return new WslProcessResult(1, "", "");
        });
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\Ubuntu\home\u\.codex",
        });
        var discovery = new WslCodexHomeDiscovery(
            runner, checker,
            listTimeout: TimeSpan.FromSeconds(5),
            perDistributionTimeout: TimeSpan.FromMilliseconds(50));

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("Ubuntu", result[0].Selection.WslDistributionName);
    }

    [Fact]
    public async Task SkipsDistributionWhenSessionsDirectoryMissing()
    {
        var listOutput = "Ubuntu\nDebian\n";
        var paths = new Dictionary<string, string>
        {
            ["Ubuntu"] = "/home/u/.codex",
            ["Debian"] = "/home/d/.codex",
        };
        var runner = FakeWslProcessRunner.FromSync(HandlerForListAndPaths(listOutput, paths));
        // Only Ubuntu has a sessions/ directory; Debian does not.
        var checker = new FakeCodexHomeDirectoryChecker(new[]
        {
            @"\\wsl.localhost\Ubuntu\home\u\.codex",
        });
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        var resolved = Assert.Single(result);
        Assert.Equal("Ubuntu", resolved.Selection.WslDistributionName);
        // The checker must still have been probed for Debian.
        Assert.Contains(@"\\wsl.localhost\Debian\home\d\.codex", checker.Checks);
    }

    [Fact]
    public async Task PreCancelledTokenReturnsEmptyWithoutCallingWsl()
    {
        var runner = FakeWslProcessRunner.FromSync((_, _, _) =>
        {
            throw new InvalidOperationException("should not be called");
        });
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var result = await discovery.DiscoverAsync(cts.Token);

        Assert.Empty(result);
        Assert.Empty(runner.Calls);
        Assert.Empty(checker.Checks);
    }

    [Fact]
    public async Task ReturnsEmptyWhenListOutputIsEmpty()
    {
        var runner = FakeWslProcessRunner.FromSync((args, _, _) =>
        {
            if (args.Count == 2 && args[0] == "--list" && args[1] == "--quiet")
                return ListOk("");
            return new WslProcessResult(1, "", "");
        });
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        var result = await discovery.DiscoverAsync(CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public async Task ConstructorDoesNotStartWsl()
    {
        // Constructing the discovery must not invoke the runner or the checker.
        // The handler is rigged to throw if ever called, so any accidental
        // invocation during construction would surface as a test failure on
        // the next DiscoverAsync call (and the Calls list would be non-empty).
        var runner = FakeWslProcessRunner.FromSync((_, _, _) =>
        {
            throw new InvalidOperationException("constructor must not call runner");
        });
        var checker = new FakeCodexHomeDirectoryChecker(Array.Empty<string>());
        var discovery = new WslCodexHomeDiscovery(runner, checker);

        Assert.Empty(runner.Calls);
        Assert.Empty(checker.Checks);
        await Task.CompletedTask;
    }

    [Fact]
    public void ConstructorRejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new WslCodexHomeDiscovery(null!, new FakeCodexHomeDirectoryChecker(Array.Empty<string>())));
        Assert.Throws<ArgumentNullException>(() =>
            new WslCodexHomeDiscovery(
                FakeWslProcessRunner.FromSync((_, _, _) => ListOk("")),
                null!));
    }
}
