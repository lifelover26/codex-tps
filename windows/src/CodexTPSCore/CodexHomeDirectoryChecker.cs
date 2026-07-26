using System;
using System.IO;

namespace CodexTPSCore;

// Default ICodexHomeDirectoryChecker that probes the local filesystem. Used
// for both Windows paths and \\wsl.localhost\<dist>\... UNC paths; .NET's
// Directory.Exists handles both. All exceptions (illegal path characters,
// unreachable UNC, permission errors) collapse to "directory not available"
// so the discovery loop simply skips that candidate.
public sealed class CodexHomeDirectoryChecker : ICodexHomeDirectoryChecker
{
    public bool SessionsDirectoryExists(string codexHome)
    {
        if (string.IsNullOrWhiteSpace(codexHome))
        {
            return false;
        }

        try
        {
            var sessionsPath = Path.Combine(codexHome, "sessions");
            return Directory.Exists(sessionsPath);
        }
        catch
        {
            // ArgumentException / PathTooLongException / SecurityException /
            // IOException for unreachable UNC: treat as missing.
            return false;
        }
    }
}
