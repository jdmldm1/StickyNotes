using System.Configuration;
using System.Data;
using System.Windows;

namespace StickyNotes__;

public partial class App : System.Windows.Application
{
    static App()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
    }
}

