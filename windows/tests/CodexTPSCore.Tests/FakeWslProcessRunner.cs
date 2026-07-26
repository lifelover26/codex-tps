using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CodexTPSCore;

namespace CodexTPSCore.Tests;

// Fake IWslProcessRunner for unit tests. Records every RunAsync call so tests
// can assert on ArgumentList contents and requested encodings. The handler is
// fully under test control: it can return canned WslProcessResult values or
// throw (FileNotFoundException, OperationCanceledException, etc.) to simulate
// wsl.exe missing, timeouts, and failures.
//
// This type never invokes a real process. Tests using it are deterministic and
// sandbox-safe.
//
// A single async-only constructor is used deliberately. Two overloads (one for
// Func<..., T> and one for Func<..., Task<T>>) make overload resolution
// ambiguous for many lambdas, so callers wrap synchronous results in
// Task.FromResult or pass an async lambda directly.
public sealed class FakeWslProcessRunner : IWslProcessRunner
{
    private readonly Func<IReadOnlyList<string>, Encoding, CancellationToken, Task<WslProcessResult>> _handler;

    public FakeWslProcessRunner(
        Func<IReadOnlyList<string>, Encoding, CancellationToken, Task<WslProcessResult>> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public List<RecordedCall> Calls { get; } = new();

    public async Task<WslProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        Encoding standardOutputEncoding,
        CancellationToken cancellationToken)
    {
        // Snapshot the argument list so later mutations by the caller cannot
        // rewrite history. The list is what tests assert against to verify
        // that a distribution name with spaces stayed a single argv element.
        var snapshot = arguments.ToList();
        Calls.Add(new RecordedCall(snapshot, standardOutputEncoding, cancellationToken));
        return await _handler(snapshot, standardOutputEncoding, cancellationToken).ConfigureAwait(false);
    }

    // Convenience for synchronous canned responses. Wraps the result in a
    // completed task. If the handler throws, the exception surfaces as a
    // faulted task from RunAsync, matching how the real WslProcessRunner
    // would surface the same failure.
    public static FakeWslProcessRunner FromSync(
        Func<IReadOnlyList<string>, Encoding, CancellationToken, WslProcessResult> handler)
    {
        if (handler is null)
        {
            throw new ArgumentNullException(nameof(handler));
        }
        return new FakeWslProcessRunner((args, enc, ct) => Task.FromResult(handler(args, enc, ct)));
    }

    public sealed record RecordedCall(
        IReadOnlyList<string> Arguments,
        Encoding Encoding,
        CancellationToken CancellationToken);
}
