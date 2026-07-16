using System;
using System.IO;

namespace CodexTPSTray;

public sealed class SessionFolderLauncher
{
    private readonly Func<string> _sessionsRootProvider;
    private readonly IShellLauncher _shellLauncher;

    public SessionFolderLauncher(Func<string> sessionsRootProvider, IShellLauncher shellLauncher)
    {
        _sessionsRootProvider = sessionsRootProvider;
        _shellLauncher = shellLauncher;
    }

    public bool OpenSessionsFolder()
    {
        string sessionsRoot = _sessionsRootProvider();

        if (!Directory.Exists(sessionsRoot))
        {
            return false;
        }

        try
        {
            return _shellLauncher.Launch(sessionsRoot);
        }
        catch
        {
            return false;
        }
    }
}