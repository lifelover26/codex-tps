using System;
using System.Diagnostics;

namespace CodexTPSTray;

public sealed class ShellLauncher : IShellLauncher
{
    public bool Launch(string path)
    {
        try
        {
            var startInfo = new ProcessStartInfo(path)
            {
                UseShellExecute = true
            };

            return Process.Start(startInfo) is not null;
        }
        catch
        {
            return false;
        }
    }
}