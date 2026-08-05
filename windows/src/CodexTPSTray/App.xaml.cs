using System;
using System.Windows;

namespace CodexTPSTray;

public partial class App : System.Windows.Application
{
    private SingleInstanceGuard? _instanceGuard;
    private TrayIconManager? _trayIconManager;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // DPI awareness is declared authoritatively in app.manifest (PerMonitorV2).
        // These WinForms calls are required for correct tray icon / context menu rendering
        // in the mixed WPF + WinForms application even though the entry point is WPF.
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);

        _instanceGuard = new SingleInstanceGuard("Local\\gaofeng21cn.CodexTPS.SingleInstance");

        if (!_instanceGuard.IsOwned)
        {
            _instanceGuard.Dispose();
            Shutdown();
            return;
        }

        _trayIconManager = TrayIconManager.CreateDefault();
        _trayIconManager.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayIconManager?.Dispose();
        _instanceGuard?.Dispose();
        base.OnExit(e);
    }
}