using System;
using System.Windows;

namespace CodexTPSTray;

public partial class App : System.Windows.Application
{
    private TrayIconManager? _trayIconManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _trayIconManager = new TrayIconManager();
        _trayIconManager.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        base.OnExit(e);
    }
}