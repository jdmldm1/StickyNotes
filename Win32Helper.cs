using System;
using System.Runtime.InteropServices;
using System.Text;

namespace StickyNotes__
{
    public static class Win32Helper
    {
        // AppBar API
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct APPBARDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uCallbackMessage;
            public int uEdge;
            public RECT rc;
            public IntPtr lParam;
        }

        public const int ABM_NEW = 0;
        public const int ABM_REMOVE = 1;
        public const int ABM_QUERYPOS = 2;
        public const int ABM_SETPOS = 3;
        public const int ABM_ACTIVATE = 6;

        public const int ABE_LEFT = 0;
        public const int ABE_TOP = 1;
        public const int ABE_RIGHT = 2;
        public const int ABE_BOTTOM = 3;

        [DllImport("shell32.dll", CallingConvention = CallingConvention.StdCall)]
        public static extern IntPtr SHAppBarMessage(int dwMessage, ref APPBARDATA pData);

        // Increments every time the clipboard's content actually changes -- the only reliable way
        // to detect "is this really new" for images, since Clipboard.GetImage() hands back a fresh
        // BitmapSource instance on every call even when the underlying content hasn't changed.
        [DllImport("user32.dll")]
        public static extern int GetClipboardSequenceNumber();

        // Window styling & position
        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        public const uint SWP_NOMOVE = 0x0002;
        public const uint SWP_NOSIZE = 0x0001;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // DWM Backdrop (Mica / Dark Mode)
        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        // Hotkeys
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public const uint MOD_ALT = 0x0001;
        public const uint MOD_CONTROL = 0x0002;
        public const uint MOD_SHIFT = 0x0004;
        public const uint MOD_WIN = 0x0008;
        public const uint MOD_NOREPEAT = 0x4000;

        // Named hotkey IDs — used in both RegisterHotKey and WndProcHook.
        // Changing an ID here automatically updates both registration and dispatch.
        public const int HotkeyNewNote      = 9001;
        public const int HotkeyScreenshot   = 9002;
        public const int HotkeySpotlight    = 9003;
        public const int HotkeyBrowserTabs  = 9004;
        public const int HotkeySaveFiles    = 9005;
        public const int HotkeyMeetingNote  = 9006;
        public const int HotkeyQuickCapture = 9007;
        public const int HotkeyGraph        = 9008; // Win+Alt+G — Tag Mind Graph
        public const int HotkeyToggleSidebar = 9009; // Win+Alt+Z — Show/Hide Sidebar
        
        public static void EnableMica(IntPtr hwnd, bool darkMode)
        {
            try
            {
                int darkValue = darkMode ? 1 : 0;
                DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkValue, sizeof(int));
                
                int backdropType = 2; // Mica backdrop type
                DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            }
            catch (Exception ex)
            {
                Console.WriteLine("Mica translucency setup failed: " + ex.Message);
            }
        }

        // GDI Screen Capture -- used by CaptureWindow to BitBlt the screen into a bitmap.
        [DllImport("gdi32.dll")]
        public static extern bool BitBlt(IntPtr hdcDest, int nXDest, int nYDest, int nWidth, int nHeight, IntPtr hdcSrc, int nXSrc, int nYSrc, int dwRop);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr hgdiobj);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);

        public const int SRCCOPY = 0x00CC0020;
    }
}
