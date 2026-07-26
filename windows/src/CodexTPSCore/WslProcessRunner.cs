using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexTPSCore;

// Default IWslProcessRunner that spawns the real wsl.exe. Located via
// Environment.SpecialFolder.System (C:\Windows\System32\wsl.exe) rather than
// PATH, so a tampered PATH cannot substitute a different executable. If
// wsl.exe is not present, RunAsync throws FileNotFoundException; the caller
// (WslCodexHomeDiscovery) catches that and treats it as "no WSL available".
//
// This type is not unit-tested directly (tests use a fake runner). It is
// exercised manually on Windows. Keep it small and obvious. The cancellation
// pre-check and the KillAndWaitForExitAsync helper are the only pieces that
// are auto-tested; everything else touches the real Process API and is
// verified manually.
public sealed class WslProcessRunner : IWslProcessRunner
{
    // Finite upper bound for post-cancellation cleanup. After Kill, a
    // well-behaved process exits within milliseconds; the timeout exists only
    // to guarantee RunAsync never blocks forever if Kill fails or the OS is
    // slow to reap the process tree.
    private static readonly TimeSpan PostCancellationExitTimeout =
        TimeSpan.FromSeconds(5);

    // Finite upper bound for draining redirected stdout/stderr pipes after the
    // process has exited. The pipe EOF fires almost immediately after Kill;
    // the timeout only guards against a stuck runtime.
    private static readonly TimeSpan PostExitDrainTimeout =
        TimeSpan.FromSeconds(2);

    private static readonly string WslExecutablePath = ResolveWslExecutablePath();

    public async Task<WslProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        Encoding standardOutputEncoding,
        CancellationToken cancellationToken)
    {
        // Pre-check: never spawn a process we already know we must cancel.
        // Doing this before the File.Exists check means a pre-cancelled token
        // surfaces as OperationCanceledException (not FileNotFoundException)
        // even on machines where wsl.exe is not installed.
        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(WslExecutablePath))
        {
            throw new FileNotFoundException(
                "wsl.exe was not found in the system directory.", WslExecutablePath);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = WslExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            // stderr is never parsed for content; decode it as UTF-8 best-effort
            // so malformed bytes never throw inside the read loop.
            StandardErrorEncoding = Encoding.UTF8,
        };
        startInfo.StandardOutputEncoding = standardOutputEncoding ?? Encoding.UTF8;

        // ArgumentList quotes/escapes each entry per the Windows command-line
        // parsing rules. A distribution name with spaces or embedded quotes
        // stays a single argv element for wsl.exe.
        foreach (var arg in arguments)
        {
            startInfo.ArgumentList.Add(arg ?? string.Empty);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();
        var stdoutDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stdoutDone.TrySetResult(true);
            }
            else
            {
                // AppendLine preserves the per-line structure that the parser
                // expects; wsl --list output is line-oriented.
                stdoutBuilder.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                stderrDone.TrySetResult(true);
            }
            else
            {
                stderrBuilder.AppendLine(e.Data);
            }
        };

        // Register the kill callback BEFORE starting the process. This closes
        // the race where the token fires between Start and Register: if the
        // token is already cancelled when Register runs, the callback invokes
        // synchronously here (and Kill throws InvalidOperationException because
        // the process has not started yet, which we swallow). The post-Start
        // cancellation check below then catches that case and kills the freshly
        // started process tree so it cannot be orphaned.
        using var registration = cancellationToken.Register(() =>
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // Already exited, or not yet started; nothing to kill here.
                // The post-Start check handles the "not yet started" case.
            }
            catch (Win32Exception)
            {
                // Cannot kill (e.g. insufficient permission); let
                // WaitForExitAsync surface the cancellation.
            }
        });

        if (!process.Start())
        {
            throw new InvalidOperationException("Failed to start wsl.exe.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // If the token was cancelled while we were setting up (between Register
        // and Start, or during Start itself), the freshly launched process tree
        // would otherwise run unsupervised until it exits on its own. Kill it
        // now, wait for exit, drain the pipes, and surface the cancellation.
        // The original OperationCanceledException is preserved: the catch block
        // below swallows nothing else, and KillAndWaitForExitAsync never throws
        // for the cancellation path.
        if (cancellationToken.IsCancellationRequested)
        {
            await KillAndWaitForExitAsync(
                () => process.Kill(entireProcessTree: true),
                token => process.WaitForExitAsync(token),
                PostCancellationExitTimeout).ConfigureAwait(false);
            await DrainPipesAsync(stdoutDone.Task, stderrDone.Task, PostExitDrainTimeout)
                .ConfigureAwait(false);
            throw new OperationCanceledException(cancellationToken);
        }

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Don't rely solely on the token-registration callback: it may have
            // raced with concurrent exit or never fired (e.g. cancel during
            // WaitForExitAsync itself). Kill again — Kill is idempotent on
            // already-exited processes per the framework contract — then wait
            // for exit with a finite, non-cancelled token so we know the OS
            // has reaped the process before we touch the pipes.
            await KillAndWaitForExitAsync(
                () => process.Kill(entireProcessTree: true),
                token => process.WaitForExitAsync(token),
                PostCancellationExitTimeout).ConfigureAwait(false);

            // Only after the process has actually exited do we drain the
            // redirected streams. After Kill + exit, the pipe EOF fires and
            // the null Data callbacks complete the TCSs.
            await DrainPipesAsync(stdoutDone.Task, stderrDone.Task, PostExitDrainTimeout)
                .ConfigureAwait(false);
            throw;
        }

        // Wait for the async read handlers to flush the final lines. Without
        // this, the last line of stdout can be lost when ExitCode is read.
        await DrainPipesAsync(stdoutDone.Task, stderrDone.Task, PostExitDrainTimeout)
            .ConfigureAwait(false);

        return new WslProcessResult(
            process.ExitCode,
            stdoutBuilder.ToString(),
            stderrBuilder.ToString());
    }

    // Idempotent kill + bounded wait-for-exit used on every cancellation path.
    // Wraps Process.Kill / WaitForExitAsync behind injectable delegates so the
    // cancellation cleanup contract can be unit-tested without spawning real
    // processes. Exposed as internal static to allow direct testing.
    //
    // Contract:
    //   - Swallows InvalidOperationException and Win32Exception from `kill`:
    //     the process may have already exited, may not have started yet, or
    //     may be unkillable due to permission. None of these may mask the
    //     original OperationCanceledException raised by the caller.
    //   - Waits for exit using a fresh, internally-owned cancellation token
    //     derived from `cleanupTimeout`. The caller's (already-cancelled)
    //     token is never passed to WaitForExitAsync, so the wait can actually
    //     observe process exit instead of throwing immediately.
    //   - If `waitForExitAsync` throws OperationCanceledException (cleanup
    //     timeout), it is swallowed. The original cancellation is still
    //     surfaced by the caller's `throw;` after this returns.
    //   - Any other exception from `waitForExitAsync` is swallowed for the
    //     same reason: the original cancellation must not be masked.
    internal static async Task KillAndWaitForExitAsync(
        Action kill,
        Func<CancellationToken, Task> waitForExitAsync,
        TimeSpan cleanupTimeout)
    {
        if (kill is null)
        {
            throw new ArgumentNullException(nameof(kill));
        }
        if (waitForExitAsync is null)
        {
            throw new ArgumentNullException(nameof(waitForExitAsync));
        }
        if (cleanupTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cleanupTimeout));
        }

        try
        {
            kill();
        }
        catch (InvalidOperationException)
        {
            // Already exited or not yet started. Nothing to kill.
        }
        catch (Win32Exception)
        {
            // Cannot kill (e.g. insufficient permission). Best effort.
        }

        using var cleanupCts = new CancellationTokenSource(cleanupTimeout);
        try
        {
            await waitForExitAsync(cleanupCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cleanup timeout. The process did not exit in time. Surface
            // nothing here; the caller's original cancellation must remain
            // the visible failure.
        }
        catch
        {
            // Any other surprise (e.g. ObjectDisposedException if the Process
            // was disposed concurrently) must not mask the original
            // cancellation. Swallow; the caller rethrows.
        }
    }

    // Drains the redirected stdout/stderr pipes after the process has exited.
    // Bounded so a stuck runtime can never block RunAsync forever.
    private static async Task DrainPipesAsync(
        Task stdoutDone,
        Task stderrDone,
        TimeSpan timeout)
    {
        using var drainCts = new CancellationTokenSource(timeout);
        try
        {
            await Task.WhenAll(stdoutDone, stderrDone)
                .WaitAsync(drainCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Drain timeout. The pipes did not close in time; best effort.
        }
        catch
        {
            // Don't let a faulted TCS mask the original exception path.
        }
    }

    private static string ResolveWslExecutablePath()
    {
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        if (string.IsNullOrWhiteSpace(systemDir))
        {
            // Fallback: let CreateProcess resolve via PATH. This path is only
            // taken when the System folder is unavailable, which is not a
            // realistic Windows desktop scenario.
            return "wsl.exe";
        }
        return Path.Combine(systemDir, "wsl.exe");
    }
}
