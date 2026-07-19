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
        private Win32Helper.APPBARDATA _appBarData;
        private bool _isAppBarRegistered;
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var wndHelper = new WindowInteropHelper(this);
            HwndSource source = HwndSource.FromHwnd(wndHelper.Handle);
            source.AddHook(WndProcHook);

            _spotlightWnd = new SpotlightWindow(this);
            _quickCaptureWnd = new QuickCaptureWindow(this);

            RegisterAppBar();

            ComponentDispatcher.ThreadFilterMessage += ComponentDispatcher_ThreadFilterMessage;
            RegisterAllHotKeys();

            Win32Helper.DwmSetWindowAttribute(wndHelper.Handle, Win32Helper.DWMWA_USE_IMMERSIVE_DARK_MODE, ref _darkModeValue, sizeof(int));
        }
        private int _darkModeValue = 1;
        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_ACTIVATE = 0x0006;

            if (msg == WM_ACTIVATE)
            {
                if (_isAppBarRegistered)
                {
                    Win32Helper.SHAppBarMessage(Win32Helper.ABM_ACTIVATE, ref _appBarData);
                }
            }

            return IntPtr.Zero;
        }
        private void RegisterAllHotKeys()
        {
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyNewNote, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x4E);
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyScreenshot, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x53);
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeySpotlight, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x20);
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyBrowserTabs, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x54);
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeySaveFiles, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x46);
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyMeetingNote, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x4D);
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyQuickCapture, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x51);
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyGraph, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x47);
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyToggleSidebar, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x5A);
        }
        private void UnregisterAllHotKeys()
        {
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyNewNote);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyScreenshot);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeySpotlight);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyBrowserTabs);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeySaveFiles);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyMeetingNote);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyQuickCapture);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyGraph);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyToggleSidebar);
        }
        private void ComponentDispatcher_ThreadFilterMessage(ref MSG msg, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg.message == WM_HOTKEY)
            {
                int id = msg.wParam.ToInt32();
                if (id == Win32Helper.HotkeyNewNote)
                {
                    CreateNewNote();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyScreenshot)
                {
                    TakeRegionScreenshot();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeySpotlight)
                {
                    ToggleSpotlight();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyBrowserTabs)
                {
                    SaveBrowserTabs();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeySaveFiles)
                {
                    SaveFilesToNewNote();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyMeetingNote)
                {
                    CreateQuickMeetingNote();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyQuickCapture)
                {
                    ToggleQuickCapture();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyGraph)
                {
                    GraphButton_Click(this, new RoutedEventArgs());
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyToggleSidebar)
                {
                    ToggleSidebar();
                    handled = true;
                }
            }
        }
        private void RegisterAppBar()
        {
            if (_isAppBarRegistered) return;

            var wndHelper = new WindowInteropHelper(this);
            if (wndHelper.Handle == IntPtr.Zero) return;

            _appBarData = new Win32Helper.APPBARDATA();
            _appBarData.cbSize = Marshal.SizeOf(typeof(Win32Helper.APPBARDATA));
            _appBarData.hWnd = wndHelper.Handle;
            _appBarData.uCallbackMessage = 0x8000 + 101;
            _appBarData.uEdge = Win32Helper.ABE_RIGHT;

            Win32Helper.SHAppBarMessage(Win32Helper.ABM_NEW, ref _appBarData);
            _isAppBarRegistered = true;

            SetAppBarPosition(350);
        }
        private void UnregisterAppBar()
        {
            if (_isAppBarRegistered)
            {
                Win32Helper.SHAppBarMessage(Win32Helper.ABM_REMOVE, ref _appBarData);
                _isAppBarRegistered = false;
            }
        }
        private double _cachedDpiX = 1.0;
        private double _cachedDpiY = 1.0;
        private (double dpiX, double dpiY) GetDpiFactors()
        {
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                _cachedDpiX = source.CompositionTarget.TransformToDevice.M11;
                _cachedDpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            return (_cachedDpiX, _cachedDpiY);
        }
        private void SetAppBarPosition(int width)
        {
            if (!_isAppBarRegistered) return;

            var wndHelper = new WindowInteropHelper(this);
            var (dpiX, dpiY) = GetDpiFactors();

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            int physicalLeft = (int)((screenWidth - width) * dpiX);
            int physicalTop = 0;
            int physicalRight = (int)(screenWidth * dpiX);
            int physicalBottom = (int)(screenHeight * dpiY);

            _appBarData.rc.Left = physicalLeft;
            _appBarData.rc.Top = physicalTop;
            _appBarData.rc.Right = physicalRight;
            _appBarData.rc.Bottom = physicalBottom;

            Win32Helper.SHAppBarMessage(Win32Helper.ABM_QUERYPOS, ref _appBarData);

            _appBarData.rc.Left = physicalLeft;
            _appBarData.rc.Top = physicalTop;
            _appBarData.rc.Right = physicalRight;
            _appBarData.rc.Bottom = physicalBottom;

            Win32Helper.SHAppBarMessage(Win32Helper.ABM_SETPOS, ref _appBarData);

            if (width > 0)
            {
                this.Left = _appBarData.rc.Left / dpiX;
                this.Top = _appBarData.rc.Top / dpiY;
                this.Width = (_appBarData.rc.Right - _appBarData.rc.Left) / dpiX;
                this.Height = (_appBarData.rc.Bottom - _appBarData.rc.Top) / dpiY;

                Win32Helper.SetWindowPos(
                    wndHelper.Handle,
                    IntPtr.Zero,
                    _appBarData.rc.Left,
                    _appBarData.rc.Top,
                    _appBarData.rc.Right - _appBarData.rc.Left,
                    _appBarData.rc.Bottom - _appBarData.rc.Top,
                    0
                );
            }
        }
    }
}
