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
            InitializePeekListeners();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (SettingsService.Current.SidebarWidth >= 260 && SettingsService.Current.SidebarWidth <= 800)
            {
                ApplySidebarWidth(SettingsService.Current.SidebarWidth);
            }

            string savedCardSize = string.IsNullOrEmpty(SettingsService.Current.CardSize) ? "Medium" : SettingsService.Current.CardSize;
            SetCardSize(savedCardSize);

            RefreshTagsFilter();
            LoadSavedOpacity();
            CheckStaleNotes();

            _recentNoteIds = SettingsService.Current.RecentNoteIds ?? new List<int>();
            RenderRecentNotes();
            RenderSavedPresets();
            RenderCategoryJumpRail();
            if (SettingsService.Current.ShowHeatmap)
            {
                HeatmapContainer.Visibility = Visibility.Visible;
                RenderActivityHeatmap();
            }

            if (SettingsService.Current.StartCollapsed)
            {
                CollapseSidebar(animate: false);
            }
        }
    }
}
