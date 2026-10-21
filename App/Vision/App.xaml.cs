using System;
using System.Threading;
using System.Windows;

namespace PlateVision50;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        try {
            _singleInstance=new Mutex(true, @"Local\DATN_AML_LINE_CONTROL_UI", out bool isNew);
            if(!isNew)
            {
                Console.WriteLine("[App] Mutex Local\\DATN_AML_LINE_CONTROL_UI is already held by another process; shutting down.");
                Shutdown();
                return;
            }
        } catch(Exception ex) {
            Console.WriteLine("[App] Mutex error: " + ex);
        }
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try { _singleInstance?.ReleaseMutex(); } catch(ApplicationException) { }
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
