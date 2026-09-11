using System.Configuration;
using System.Data;
using System.Diagnostics;
using System.Threading;
using System.Windows;

namespace StickyNotes__;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstanceMutex;
    private bool _hasHandle = false;

    static App()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
        {
            Debug.WriteLine($"Unhandled exception: {ev.ExceptionObject}");
        };
        DispatcherUnhandledException += (s, ev) =>
        {
            Debug.WriteLine($"Dispatcher exception: {ev.Exception}");
        };

        try
        {
            _singleInstanceMutex = new Mutex(true, "StickyNotesPlusPlus_SingleInstance_Mutex", out bool createdNew);
            _hasHandle = createdNew;
            if (!createdNew)
            {
                MessageBox.Show("StickyNotes++ is already running. Check your system tray or taskbar.",
                    "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }
        }
        catch
        {
            _hasHandle = false;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_hasHandle && _singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { }
            try { _singleInstanceMutex.Dispose(); } catch { }
        }
        base.OnExit(e);
    }
}
