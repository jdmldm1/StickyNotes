using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Threading.Tasks;


namespace StickyNotes__
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                _cachedDpiX = dpi.DpiScaleX;
                _cachedDpiY = dpi.DpiScaleY;
            }
            catch {}
            DatabaseHelper.InitDatabase();
            InitializeNotifyIcon();
            StartClipboardMonitor();
            ApplyJeffsNotesSyncSettings();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshNotesList();
            RefreshTagsFilter();
            LoadSavedOpacity();
            CheckStaleNotes();
        }
    }
}
