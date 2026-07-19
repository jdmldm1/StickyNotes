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
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private void InitializeNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            try
            {
                var iconUri = new Uri("pack://application:,,,/app_icon.ico");
                var streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo?.Stream != null)
                    _notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                else
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            _notifyIcon.Text = "StickyNotes++";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            
            var openItem = new System.Windows.Forms.ToolStripMenuItem("Open StickyNotes++");
            openItem.Click += (s, e) => RestoreFromTray();
            contextMenu.Items.Add(openItem);
            
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }
        private void RestoreFromTray()
        {
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            this.Left = screenWidth - 350;
            this.Top = 0;
            this.Width = 350;
            this.Height = screenHeight;

            RegisterAppBar();
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }
        private void ExitApplication()
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;

            var openWindows = new List<NoteWindow>(_openNoteWindows.Values);
            foreach (var noteWnd in openWindows)
            {
                try { noteWnd.Close(); } catch {}
            }

            try { _noteManagerWnd?.Close(); } catch { }
            try { _graphWnd?.Close(); } catch { }

            Application.Current.Shutdown();
        }
        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                UnregisterAppBar();
                this.Hide();
                this.WindowState = WindowState.Normal;
            }
            base.OnStateChanged(e);
        }
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnClosing(e);
        }
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            UnregisterAppBar();
            this.Hide();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }
        public void ApplySidebarOpacity(double opacity)
        {
            opacity = Math.Max(0.2, Math.Min(1.0, opacity));
            byte alpha = (byte)(opacity * 255);
            MainWindowBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(alpha, 0x12, 0x12, 0x12));
        }
        private void LoadSavedOpacity()
        {
            try
            {
                var config = SettingsService.Current;
                if (config.SidebarOpacity > 0)
                    ApplySidebarOpacity(config.SidebarOpacity);
            }
            catch { }
        }
        private DispatcherTimer? _statusToastTimer;
        public void ShowStatusToast(string message, int durationMs = 2800)
        {
            StatusToastText.Text = message;
            StatusToast.Visibility = Visibility.Visible;

            _statusToastTimer?.Stop();
            _statusToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            _statusToastTimer.Tick += (s, e) =>
            {
                StatusToast.Visibility = Visibility.Collapsed;
                _statusToastTimer?.Stop();
            };
            _statusToastTimer.Start();
        }

        private void NewNoteButton_Click(object sender, RoutedEventArgs e) => CreateNewNote();

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e) => TakeRegionScreenshot();

        private void SaveTabsButton_Click(object sender, RoutedEventArgs e) => SaveBrowserTabs();

        private void AddFileButton_Click(object sender, RoutedEventArgs e) => SaveFilesToNewNote();

        private void MeetingNoteButton_Click(object sender, RoutedEventArgs e) => CreateQuickMeetingNote();
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            var openWindows = _openNoteWindows.Values.ToList();
            foreach (var wnd in openWindows)
            {
                wnd.Close();
            }

            try
            {
                _spotlightWnd?.Close();
                _quickCaptureWnd?.Close();
            }
            catch {}

            UnregisterAppBar();

            ComponentDispatcher.ThreadFilterMessage -= ComponentDispatcher_ThreadFilterMessage;
            UnregisterAllHotKeys();

            _clipboardTimer?.Stop();
        }
    }
}
