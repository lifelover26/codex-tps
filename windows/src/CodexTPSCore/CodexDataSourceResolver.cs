using System;
using System.Threading;
using System.Threading.Tasks;

namespace CodexTPSCore;

// Turns a durable CodexDataSourceSelection into a concrete, run-scoped
// ResolvedCodexDataSource (CodexHome + DisplayName). The selection is what the
// user chose and what is persisted; the resolution is recomputed on every run
// because a WSL distribution's UNC prefix can change across reboots or after
// wsl --import/--export.
//
// Contract:
//   - Windows resolves synchronously and never touches wsl.exe.
//   - WSL resolves exactly the one selected distribution; it does NOT run
//     `wsl --list --quiet` and does NOT probe other distributions.
//   - Any resolution failure (WSL unavailable, timeout, cancellation, missing
//     sessions directory, illegal path) returns null. Nothing is thrown to the
//     caller for runtime failures; only a null `selection` argument (a
//     programming error) throws ArgumentNullException.
//   - The resolved UNC path is never cached or persisted by this type.
public interface ICodexDataSourceResolver
{
    Task<ResolvedCodexDataSource?> ResolveAsync(
        CodexDataSourceSelection selection,
        CancellationToken cancellationToken);
}

// Default ICodexDataSourceResolver. Windows is handled inline; WSL is delegated
// to WslCodexHomeDiscovery.ResolveSingleDistributionAsync so the safe wsl.exe
// command, timeout, UNC conversion, and sessions-directory check are reused
// rather than duplicated.
public sealed class CodexDataSourceResolver : ICodexDataSourceResolver
{
    private readonly WslCodexHomeDiscovery _wslDiscovery;

    public CodexDataSourceResolver(WslCodexHomeDiscovery wslDiscovery)
    {
        _wslDiscovery = wslDiscovery ?? throw new ArgumentNullException(nameof(wslDiscovery));
    }

    public async Task<ResolvedCodexDataSource?> ResolveAsync(
        CodexDataSourceSelection selection,
        CancellationToken cancellationToken)
    {
        if (selection is null)
        {
            throw new ArgumentNullException(nameof(selection));
        }

        switch (selection.Kind)
        {
            case CodexDataSourceKind.Windows:
                // Windows resolves immediately: no wsl.exe, no probes, and no
                // requirement that the sessions directory pre-exists. CodexHome
                // follows SessionScanner.DefaultCodexHome() (CODEX_HOME env var
                // or %USERPROFILE%\.codex) so the existing scanner semantics —
                // including a missing sessions dir surfacing as
                // SessionsDirectoryMissing rather than as a resolution failure
                // — are preserved exactly. DisplayName is a stable, path-free
                // "Windows" string.
                return new ResolvedCodexDataSource(
                    Selection: selection,
                    DisplayName: "Windows",
                    CodexHome: SessionScanner.DefaultCodexHome());

            case CodexDataSourceKind.Wsl:
                var name = selection.WslDistributionName;
                if (string.IsNullOrEmpty(name))
                {
                    // CodexDataSourceSelection enforces a non-null name for WSL,
                    // so this branch is only reachable if a tampered instance
                    // bypassed construction validation. Fail safe: no resolution.
                    return null;
                }
                // Direct single-source resolution: no `wsl --list`, no probing
                // of other distributions. ResolveSingleDistributionAsync applies
                // the per-distribution timeout and the same path/sessions checks
                // as DiscoverAsync, and returns null on any failure.
                return await _wslDiscovery
                    .ResolveSingleDistributionAsync(name, cancellationToken)
                    .ConfigureAwait(false);

            default:
                // Undefined enum value (only possible via an invalid cast).
                return null;
        }
    }
}
