using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexTPSCore;
using Xunit;

namespace CodexTPSCore.Tests;

// WslProcessRunner is not unit-tested end-to-end because it spawns the real
// wsl.exe (see the type-level comment in WslProcessRunner.cs). The two pieces
// that ARE unit-testable live here:
//   - Pre-start cancellation check: a pre-cancelled token must throw
//     OperationCanceledException before the runner checks for wsl.exe, so the
//     contract is observable even on machines without wsl.exe installed.
//   - KillAndWaitForExitAsync: the post-cancellation cleanup helper is a pure
//     function over injected delegates, so every code path (kill throws,
//     wait completes, wait times out, wait throws) can be exercised
//     deterministically without a real Process.
public class WslProcessRunnerTests
{
    [Fact]
    public async Task RunAsyncWithPreCancelledTokenThrowsOperationCanceledException()
    {
        // The runner must short-circuit before File.Exists/Process.Start so a
        // pre-cancelled caller never spawns wsl.exe. This is observable even
        // when wsl.exe is not installed: the expected exception is
        // OperationCanceledException, NOT FileNotFoundException.
        var runner = new WslProcessRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            runner.RunAsync(
                new[] { "--list", "--quiet" },
                Encoding.Unicode,
                cts.Token));
    }

    [Fact]
    public async Task RunAsyncWithPreCancelledTokenDoesNotEnumerateArguments()
    {
        // The pre-check must run before any work that touches the argument
        // list. Pass a lazy argument enumerator that throws if enumerated; if
        // the runner touches it before the cancellation check, this test
        // fails with InvalidOperationException instead of the expected
        // OperationCanceledException.
        var runner = new WslProcessRunner();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var throwingArguments = new ThrowingReadOnlyList();
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            runner.RunAsync(throwingArguments, Encoding.Unicode, cts.Token));
    }

    [Fact]
    public async Task KillAndWaitForExitAsyncSwallowsInvalidOperationExceptionFromKill()
    {
        // The process may have already exited (or never started) before the
        // cancellation callback fires. Process.Kill surfaces that as
        // InvalidOperationException; the helper must swallow it so the
        // caller's original OperationCanceledException remains the visible
        // failure.
        var killCalled = false;
        var waitCalled = false;

        await WslProcessRunner.KillAndWaitForExitAsync(
            kill: () =>
            {
                killCalled = true;
                throw new InvalidOperationException();
            },
            waitForExitAsync: _ =>
            {
                waitCalled = true;
                return Task.CompletedTask;
            },
            cleanupTimeout: TimeSpan.FromSeconds(1));

        Assert.True(killCalled);
        Assert.True(waitCalled);
    }

    [Fact]
    public async Task KillAndWaitForExitAsyncSwallowsWin32ExceptionFromKill()
    {
        // Insufficient permission to kill surfaces as Win32Exception. The
        // helper must swallow it for the same reason as above.
        var killCalled = false;
        var waitCalled = false;

        await WslProcessRunner.KillAndWaitForExitAsync(
            kill: () =>
            {
                killCalled = true;
                throw new Win32Exception();
            },
            waitForExitAsync: _ =>
            {
                waitCalled = true;
                return Task.CompletedTask;
            },
            cleanupTimeout: TimeSpan.FromSeconds(1));

        Assert.True(killCalled);
        Assert.True(waitCalled);
    }

    [Fact]
    public async Task KillAndWaitForExitAsyncWaitsForExitAfterKill()
    {
        // After a successful Kill, the helper must await process exit before
        // returning, so the caller can safely drain pipes knowing the OS has
        // reaped the process.
        var callOrder = 0;
        var killOrder = 0;
        var waitOrder = 0;

        await WslProcessRunner.KillAndWaitForExitAsync(
            kill: () => killOrder = ++callOrder,
            waitForExitAsync: _ =>
            {
                waitOrder = ++callOrder;
                return Task.CompletedTask;
            },
            cleanupTimeout: TimeSpan.FromSeconds(1));

        Assert.Equal(1, killOrder);
        Assert.Equal(2, waitOrder);
    }

    [Fact]
    public async Task KillAndWaitForExitAsyncTimesOutWhenProcessNeverExits()
    {
        // If waitForExitAsync never completes, the helper must give up after
        // cleanupTimeout so RunAsync never blocks forever. The helper must
        // return normally (it must not throw), preserving the caller's
        // original OperationCanceledException as the visible failure.
        var waitStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var neverCompletes = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var startTime = DateTimeOffset.UtcNow;

        await WslProcessRunner.KillAndWaitForExitAsync(
            kill: () => { },
            waitForExitAsync: async token =>
            {
                waitStarted.TrySetResult(true);
                try
                {
                    await neverCompletes.Task.WaitAsync(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The cleanup token fired; surface this as a completed
                    // task so the helper observes the timeout path.
                }
            },
            cleanupTimeout: TimeSpan.FromMilliseconds(75));

        var elapsed = DateTimeOffset.UtcNow - startTime;

        Assert.True(waitStarted.Task.IsCompleted);
        // The helper must not return before the cleanup timeout fires (allow a
        // small floor to absorb scheduling jitter).
        Assert.True(elapsed >= TimeSpan.FromMilliseconds(40),
            $"elapsed={elapsed} should be at least ~40ms");
        // And it must not run away with the caller's thread for too long.
        Assert.True(elapsed < TimeSpan.FromSeconds(3),
            $"elapsed={elapsed} should be under 3s");
    }

    [Fact]
    public async Task KillAndWaitForExitAsyncSwallowsUnexpectedExceptionFromWait()
    {
        // If the wait itself throws something other than OperationCanceled
        // (e.g. ObjectDisposedException because the Process was disposed
        // concurrently), the helper must still not mask the caller's
        // original OperationCanceledException. Swallow and return.
        var killCalled = false;

        await WslProcessRunner.KillAndWaitForExitAsync(
            kill: () => killCalled = true,
            waitForExitAsync: _ => throw new ObjectDisposedException("process"),
            cleanupTimeout: TimeSpan.FromSeconds(1));

        Assert.True(killCalled);
    }

    [Fact]
    public async Task KillAndWaitForExitAsyncRejectsNullKill()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            WslProcessRunner.KillAndWaitForExitAsync(
                kill: null!,
                waitForExitAsync: _ => Task.CompletedTask,
                cleanupTimeout: TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public async Task KillAndWaitForExitAsyncRejectsNullWait()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            WslProcessRunner.KillAndWaitForExitAsync(
                kill: () => { },
                waitForExitAsync: null!,
                cleanupTimeout: TimeSpan.FromSeconds(1)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task KillAndWaitForExitAsyncRejectsNonPositiveTimeout(int seconds)
    {
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            WslProcessRunner.KillAndWaitForExitAsync(
                kill: () => { },
                waitForExitAsync: _ => Task.CompletedTask,
                cleanupTimeout: TimeSpan.FromSeconds(seconds)));
    }

    // IReadOnlyList that throws if anything tries to enumerate or index it.
    // Used to prove that RunAsync's pre-cancellation check happens before the
    // argument list is ever touched.
    private sealed class ThrowingReadOnlyList : IReadOnlyList<string>
    {
        public string this[int index] =>
            throw new InvalidOperationException("arguments must not be enumerated");
        public int Count =>
            throw new InvalidOperationException("arguments must not be enumerated");
        public IEnumerator<string> GetEnumerator() =>
            throw new InvalidOperationException("arguments must not be enumerated");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
            GetEnumerator();
    }
}
