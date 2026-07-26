using System;
using System.Collections.Generic;
using CodexTPSCore;

namespace CodexTPSCore.Tests;

// Fake ICodexHomeDirectoryChecker. The test populates the constructor with the
// set of CodexHome paths whose sessions/ subdirectory should be reported as
// existing. Every call is recorded so tests can assert which candidates were
// probed and in what order.
//
// Never touches the real filesystem, so WSL UNC paths that are unreachable
// inside CI/sandbox can still be tested deterministically.
public sealed class FakeCodexHomeDirectoryChecker : ICodexHomeDirectoryChecker
{
    private readonly HashSet<string> _homesWithSessions;

    public FakeCodexHomeDirectoryChecker(IEnumerable<string> homesWithSessions)
    {
        _homesWithSessions = new HashSet<string>(
            homesWithSessions ?? Enumerable.Empty<string>(),
            StringComparer.Ordinal);
    }

    public List<string> Checks { get; } = new();

    public bool SessionsDirectoryExists(string codexHome)
    {
        Checks.Add(codexHome);
        return _homesWithSessions.Contains(codexHome);
    }
}
