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

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            return false;
        }
    }
}