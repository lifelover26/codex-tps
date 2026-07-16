using CodexTPSTray;
using System.Collections.Generic;

namespace CodexTPSTray.Tests;

public sealed class FakeShellLauncher : IShellLauncher
{
    public List<string> LaunchCalls { get; } = new();
    public bool LaunchResult { get; set; } = true;
    public bool ThrowException { get; set; } = false;

    public bool Launch(string path)
    {
        if (ThrowException)
        {
            throw new System.Exception("Fake launch exception");
        }

        LaunchCalls.Add(path);
        return LaunchResult;
    }
}