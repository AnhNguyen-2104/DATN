using System;
using System.Threading;
using System.Windows;

namespace PlateVision50;

public partial class App : Application
{
    private Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstance=new Mutex(true, @"Local\DATN_AML_LINE_CONTROL_UI", out bool isNew);
        if(!isNew)
        {
            MessageBox.Show("DATN Line Control đang mở trên AML-IPC.\nKhông khởi chạy thêm một bản để tránh tranh chấp Camera và HMI.",
                "DATN - Single Instance",MessageBoxButton.OK,MessageBoxImage.Information);
            Shutdown();
            return;
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
