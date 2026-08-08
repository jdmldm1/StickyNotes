using System.Configuration;
using System.Data;
using System.Threading;
using System.Windows;

namespace StickyNotes__;

public partial class App : System.Windows.Application
{
    // A second running instance would hold its own copy of the vault's session key in memory.
    // If the vault password is changed in one instance, a note marked secure in a stale second
    // instance gets encrypted with the old (now unrecoverable) key - permanently corrupting it.
    private Mutex? _singleInstanceMutex;

    static App()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, "StickyNotesPlusPlus_SingleInstance_Mutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("StickyNotes++ is already running. Check your system tray or taskbar.",
                "Already Running", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}

