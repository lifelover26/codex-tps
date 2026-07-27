using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexTPSCore;

// Discovers Codex data sources available through WSL.
//
// Lifecycle contract:
//   - The constructor only stores dependencies. It never spawns wsl.exe and
//     never touches the filesystem. Safe to construct on the UI thread.
//   - DiscoverAsync is fully async. It never calls .Wait()/.Result and never
//     blocks the caller. Cancellation is cooperative via the token.
//   - Failed results are not cached to disk: every DiscoverAsync call probes
//     WSL again. A distribution that was unavailable last time is retried.
//
// Error policy (per the data-source contract):
//   - wsl.exe missing, list returns non-zero, list times out, list output is
//     unparseable: return an empty list. Do not throw to the UI.
//   - A single distribution fails to start, returns non-zero, times out, or
//     yields an invalid path: skip it. Other distributions still resolve.
//   - A distribution whose CodexHome has no sessions/ directory is treated as
//     currently unavailable and is skipped silently.
//
// Privacy: this type only reads the value of $CODEX_HOME (or $HOME/.codex)
// and checks for the existence of a sessions/ subdirectory. It never reads
// JSONL content, never decodes prompts or responses, and never logs the
// resolved Linux username or private Linux path. The DisplayName surfaced to
// the UI contains only the distribution name, prefixed with "WSL: ".
public interface IWslCodexHomeDiscovery
{
    Task<IReadOnlyList<ResolvedCodexDataSource>> DiscoverAsync(CancellationToken cancellationToken);
    Task<ResolvedCodexDataSource?> ResolveSingleDistributionAsync(string distributionName, CancellationToken cancellationToken);
}

public sealed class WslCodexHomeDiscovery : IWslCodexHomeDiscovery
{
    // wsl --list --quiet is normally instant. Allow generous headroom for
    // first-run WSL startup without making the UI feel stuck.
    private const int DefaultListTimeoutSeconds = 10;

    // Per-distribution `sh -lc` invocation: first launch of a cold
    // distribution can take several seconds while WSL boots the VM. 15s
    // matches the WSL default boot timeout with a small margin.
    private const int DefaultPerDistributionTimeoutSeconds = 15;

    private static readonly Encoding ListOutputEncoding = Encoding.Unicode; // UTF-16LE
    private static readonly Encoding ShellOutputEncoding = Encoding.UTF8;

    private readonly IWslProcessRunner _processRunner;
    private readonly ICodexHomeDirectoryChecker _directoryChecker;
    private readonly TimeSpan _listTimeout;
    private readonly TimeSpan _perDistributionTimeout;

    public WslCodexHomeDiscovery(
        IWslProcessRunner processRunner,
        ICodexHomeDirectoryChecker directoryChecker,
        TimeSpan? listTimeout = null,
        TimeSpan? perDistributionTimeout = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _directoryChecker = directoryChecker ?? throw new ArgumentNullException(nameof(directoryChecker));
        _listTimeout = listTimeout ?? TimeSpan.FromSeconds(DefaultListTimeoutSeconds);
        _perDistributionTimeout = perDistributionTimeout ?? TimeSpan.FromSeconds(DefaultPerDistributionTimeoutSeconds);
    }

    public async Task<IReadOnlyList<ResolvedCodexDataSource>> DiscoverAsync(
        CancellationToken cancellationToken)
    {
        // If the caller already cancelled, exit fast without spawning wsl.exe.
        if (cancellationToken.IsCancellationRequested)
        {
            return Array.Empty<ResolvedCodexDataSource>();
        }

        IReadOnlyList<string> distributions;
        using (var listCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            listCts.CancelAfter(_listTimeout);
            try
            {
                distributions = await ListDistributionsAsync(listCts.Token).ConfigureAwait(false);
            }
            catch
            {
                // wsl.exe missing, non-zero exit, timeout, or output decode
                // failure: no candidates available this run.
                return Array.Empty<ResolvedCodexDataSource>();
            }
        }

        if (distributions.Count == 0)
        {
            return Array.Empty<ResolvedCodexDataSource>();
        }

        var results = new List<ResolvedCodexDataSource>();
        foreach (var distribution in distributions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            ResolvedCodexDataSource? resolved;
            using (var perDistCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                perDistCts.CancelAfter(_perDistributionTimeout);
                try
                {
                    resolved = await ResolveDistributionAsync(distribution, perDistCts.Token)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Skip this distribution; others may still succeed.
                    resolved = null;
                }
            }

            if (resolved is not null)
            {
                results.Add(resolved);
            }
        }

        return results;
    }

    // Resolves a single, already-chosen WSL distribution without first running
    // `wsl.exe --list --quiet` and without probing any other distribution.
    //
    // Used by the data-source resolver (phase 2) to turn a persisted user
    // selection directly into a concrete CodexHome for the current run. This
    // is the "resolve one" path; DiscoverAsync above is the "list all" path.
    //
    // Reuses the same private ResolveDistributionAsync as the list loop, so the
    // safe wsl.exe command, the per-distribution timeout, the UNC conversion,
    // and the sessions-directory check are identical and not duplicated.
    //
    // Failure policy matches DiscoverAsync: any failure (invalid name, wsl.exe
    // missing, non-zero exit, timeout, cancellation, illegal path output, or
    // missing sessions/ directory) returns null. Nothing is thrown to the UI.
    // The returned UNC path is re-resolved on every call; nothing is cached or
    // persisted here.
    public async Task<ResolvedCodexDataSource?> ResolveSingleDistributionAsync(
        string distributionName,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        // Validate before creating the linked CTS so a pre-checked invalid name
        // never spawns a process. ResolveDistributionAsync also validates, but
        // keeping this guard means the timeout/CTS machinery is skipped for the
        // obviously-invalid case.
        if (!WslDistributionNameValidator.IsValid(distributionName))
        {
            return null;
        }

        using var perDistCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        perDistCts.CancelAfter(_perDistributionTimeout);
        try
        {
            return await ResolveDistributionAsync(distributionName, perDistCts.Token)
                .ConfigureAwait(false);
        }
        catch
        {
            // Timeout, cancellation, wsl.exe missing, or any other runtime
            // failure: the distribution is unavailable this run. Return null
            // so the caller can fall back without surfacing an exception.
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ListDistributionsAsync(CancellationToken cancellationToken)
    {
        var result = await _processRunner.RunAsync(
            new[] { "--list", "--quiet" },
            ListOutputEncoding,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return Array.Empty<string>();
        }

        return WslDistributionListParser.Parse(result.StandardOutput);
    }

    private async Task<ResolvedCodexDataSource?> ResolveDistributionAsync(
        string distributionName,
        CancellationToken cancellationToken)
    {
        if (!WslDistributionNameValidator.IsValid(distributionName))
        {
            return null;
        }

        // Fixed command: sh -lc 'printf "%s" "${CODEX_HOME:-$HOME/.codex}"'
        // The single-quoted form in the spec is shell-level quoting for wsl's
        // own argv. Using ArgumentList we pass the literal printf command as
        // one argument without the surrounding single quotes; .NET re-quotes
        // it so wsl.exe receives exactly one argv element.
        var arguments = new[]
        {
            "--distribution",
            distributionName,
            "--exec",
            "sh",
            "-lc",
            @"printf ""%s"" ""${CODEX_HOME:-$HOME/.codex}"""
        };

        var result = await _processRunner.RunAsync(
            arguments,
            ShellOutputEncoding,
            cancellationToken).ConfigureAwait(false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        // Trim trailing whitespace/newlines that login shell startup may emit.
        // Do not trim leading whitespace: a Linux path never starts with space,
        // and trimming leading space would hide a polluted stdout (e.g. MOTD
        // printed before printf). Validation will reject such output anyway.
        var linuxPath = result.StandardOutput;
        var trailingNewline = linuxPath.Length;
        while (trailingNewline > 0)
        {
            var ch = linuxPath[trailingNewline - 1];
            if (ch == '\r' || ch == '\n' || ch == '\0' || ch == ' ' || ch == '\t')
            {
                trailingNewline--;
            }
            else
            {
                break;
            }
        }
        linuxPath = linuxPath.Substring(0, trailingNewline);

        if (!WslLinuxPathConverter.TryConvertToUnc(linuxPath, distributionName, out var uncPath))
        {
            return null;
        }

        if (!_directoryChecker.SessionsDirectoryExists(uncPath))
        {
            return null;
        }

        return new ResolvedCodexDataSource(
            Selection: CodexDataSourceSelection.ForWsl(distributionName),
            DisplayName: "WSL: " + distributionName,
            CodexHome: uncPath);
    }
}
