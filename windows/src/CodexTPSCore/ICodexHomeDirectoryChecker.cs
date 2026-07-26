namespace CodexTPSCore;

// Checks whether a resolved CodexHome has a usable sessions/ subdirectory.
// Abstracted so WslCodexHomeDiscovery can be unit-tested without touching the
// real filesystem (the WSL UNC path may be unreachable inside CI/sandbox).
public interface ICodexHomeDirectoryChecker
{
    // Returns true only when <codexHome>/sessions exists and is a directory.
    // Implementations must swallow exceptions from invalid paths so a
    // malformed UNC does not propagate to the caller.
    bool SessionsDirectoryExists(string codexHome);
}
