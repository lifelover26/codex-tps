using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CodexTPSCore;

// Captured stdout/stderr of a single wsl.exe invocation. ExitCode is the
// process exit code; StandardOutput and StandardError are already decoded
// using the encoding requested by the caller. A timed-out or killed process
// is reported via OperationCanceledException from RunAsync rather than via
// this record, so any returned record implies the process exited on its own.
public sealed record WslProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError);

// Abstracts spawning wsl.exe so unit tests can drive WslCodexHomeDiscovery
// with a fake runner instead of touching the real wsl.exe. Implementations
// must:
//   - Use ProcessStartInfo.ArgumentList for every argument (never concatenate
//     a command line string) so distribution names containing spaces or quotes
//     cannot escape into a separate argument.
//   - Set UseShellExecute=false, CreateNoWindow=true, redirect stdout/stderr.
//   - Honor the CancellationToken: on cancellation, kill the entire process
//     tree (Process.Kill(entireProcessTree: true)) before returning.
//   - Decode stdout using the requested encoding (WSL --list emits UTF-16LE;
//     per-distribution sh -c output is UTF-8).
//   - Never call .Wait()/.Result on the UI thread; the contract is async-only.
public interface IWslProcessRunner
{
    Task<WslProcessResult> RunAsync(
        IReadOnlyList<string> arguments,
        Encoding standardOutputEncoding,
        CancellationToken cancellationToken);
}
