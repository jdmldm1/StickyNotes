using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace StickyNotes__
{
    public partial class EdgeHandleWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private bool _isMouseDown;
        private bool _isDragging;
        private Point _mouseDownScreenPoint;
        private double _startWindowTop;
        private double _cachedDpiY = 1.0;
        private System.Windows.Threading.DispatcherTimer? _peekTimer;

        public EdgeHandleWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
        }

        private void StartPeekTimer()
        {
            CancelPeekTimer();
            _peekTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _peekTimer.Tick += (s, e) =>
            {
                CancelPeekTimer();
                if (!_isDragging && !_isMouseDown)
                {
                    _mainWindow.PeekSidebar();
                }
            };
            _peekTimer.Start();
        }

        private void CancelPeekTimer()
        {
            _peekTimer?.Stop();
            _peekTimer = null;
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            try
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                _cachedDpiY = dpi.DpiScaleY;
            }
            catch { _cachedDpiY = 1.0; }

            var wndHelper = new WindowInteropHelper(this);
            int darkMode = 1;
            Win32Helper.DwmSetWindowAttribute(wndHelper.Handle, Win32Helper.DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));
        }

        public void PositionOnScreen()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double workAreaTop = SystemParameters.WorkArea.Top;
            double workAreaHeight = SystemParameters.WorkArea.Height;

            double ratio = SettingsService.Current.HandleVerticalPosition;
            if (ratio < 0.0 || ratio > 1.0) ratio = 0.5;

            this.Left = screenWidth - this.Width;
            double availableHeight = workAreaHeight - this.Height - 20;
            if (availableHeight < 0) availableHeight = 0;
            double targetTop = workAreaTop + 10 + (availableHeight * ratio);
            double minTop = workAreaTop + 10;
            double maxTop = workAreaTop + workAreaHeight - this.Height - 10;
            if (maxTop < minTop) maxTop = minTop;
            this.Top = Math.Clamp(targetTop, minTop, maxTop);
        }

        private void HandleBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            if (_isDragging) return;
            HandleBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x84, 0xff));
            HandleBorder.Background = new SolidColorBrush(Color.FromArgb(0xF4, 0x24, 0x24, 0x2C));
            HandleShadow.Opacity = 0.75;
            HandleShadow.BlurRadius = 14;

            var anim = new DoubleAnimation
            {
                To = -4,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            HandleTranslate.BeginAnimation(TranslateTransform.XProperty, anim);

            if (SettingsService.Current.HoverToPeek)
            {
                StartPeekTimer();
            }
        }

        private void HandleBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            CancelPeekTimer();
            if (_isDragging) return;
            HandleBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF));
            HandleBorder.Background = new SolidColorBrush(Color.FromArgb(0xE8, 0x18, 0x18, 0x1C));
            HandleShadow.Opacity = 0.45;
            HandleShadow.BlurRadius = 10;

            var anim = new DoubleAnimation
            {
                To = 0,
                Duration = TimeSpan.FromMilliseconds(120),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            HandleTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
        }

        private void Handle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            CancelPeekTimer();
            _isMouseDown = true;
            _isDragging = false;
            _mouseDownScreenPoint = PointToScreen(e.GetPosition(this));
            _startWindowTop = this.Top;
            HandleBorder.CaptureMouse();
        }

        private void Handle_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isMouseDown) return;

            Point curScreen = PointToScreen(e.GetPosition(this));
            double deltaPhysicalY = curScreen.Y - _mouseDownScreenPoint.Y;
            double deltaY = deltaPhysicalY / _cachedDpiY;

            if (!_isDragging && Math.Abs(deltaPhysicalY) > 5)
            {
                _isDragging = true;
            }

            if (_isDragging)
            {
                double newTop = _startWindowTop + deltaY;
                double workAreaTop = SystemParameters.WorkArea.Top;
                double workAreaHeight = SystemParameters.WorkArea.Height;
                double minTop = workAreaTop + 10;
                double maxTop = workAreaTop + workAreaHeight - this.Height - 10;
                if (maxTop < minTop) maxTop = minTop;
                this.Top = Math.Clamp(newTop, minTop, maxTop);
            }
        }

        private void Handle_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isMouseDown) return;
            _isMouseDown = false;
            HandleBorder.ReleaseMouseCapture();

            if (_isDragging)
            {
                _isDragging = false;
                double workAreaTop = SystemParameters.WorkArea.Top;
                double workAreaHeight = SystemParameters.WorkArea.Height;
                double availableHeight = workAreaHeight - this.Height - 20;
                if (availableHeight > 0)
                {
                    double ratio = (this.Top - (workAreaTop + 10)) / availableHeight;
                    ratio = Math.Clamp(ratio, 0.0, 1.0);
                    var config = SettingsService.Current;
                    config.HandleVerticalPosition = ratio;
                    SettingsService.Save(config);
                }
            }
            else
            {
                _mainWindow.ExpandSidebar(animate: true);
            }
        }

        private void Handle_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            var menu = new ContextMenu();
            if (Application.Current.MainWindow?.TryFindResource(typeof(ContextMenu)) is Style menuStyle)
            {
                menu.Style = menuStyle;
            }

            var openItem = new MenuItem { Header = "📖  Open StickyNotes" };
            openItem.Click += (s, args) => _mainWindow.ExpandSidebar(animate: true);
            menu.Items.Add(openItem);

            var newNoteItem = new MenuItem { Header = "➕  New Note" };
            newNoteItem.Click += (s, args) =>
            {
                _mainWindow.ExpandSidebar(animate: true);
                _mainWindow.CreateNewNote();
            };
            menu.Items.Add(newNoteItem);

            var settingsItem = new MenuItem { Header = "⚙  Settings" };
            settingsItem.Click += (s, args) => _mainWindow.OpenSettingsWindow();
            menu.Items.Add(settingsItem);

            menu.Items.Add(new Separator());

            var hideItem = new MenuItem { Header = "—  Minimize to Tray" };
            hideItem.Click += (s, args) => _mainWindow.MinimizeToTray();
            menu.Items.Add(hideItem);

            var exitItem = new MenuItem { Header = "✕  Exit" };
            exitItem.Click += (s, args) => _mainWindow.ExitApplication();
            menu.Items.Add(exitItem);

            HandleBorder.ContextMenu = menu;
        }
    }
}
