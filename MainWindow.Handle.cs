using System;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace StickyNotes__
{
    public partial class MainWindow : Window
    {
        private EdgeHandleWindow? _edgeHandleWnd;
        private bool _isAnimating;
        private bool _isPeeking;
        private System.Windows.Threading.DispatcherTimer? _peekLeaveTimer;

        public void InitializePeekListeners()
        {
            this.MouseEnter += (s, e) =>
            {
                _peekLeaveTimer?.Stop();
            };

            this.MouseLeave += (s, e) =>
            {
                if (_isPeeking)
                {
                    if (_peekLeaveTimer == null)
                    {
                        _peekLeaveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
                        _peekLeaveTimer.Tick += (ts, te) =>
                        {
                            _peekLeaveTimer?.Stop();
                            if (_isPeeking)
                            {
                                _isPeeking = false;
                                CollapseSidebar();
                            }
                        };
                    }
                    _peekLeaveTimer.Stop();
                    _peekLeaveTimer.Start();
                }
            };

            this.PreviewMouseDown += (s, e) =>
            {
                if (_isPeeking)
                {
                    _isPeeking = false;
                    _peekLeaveTimer?.Stop();
                    if (SettingsService.Current.ReserveScreenSpace)
                    {
                        RegisterAppBar();
                        SetAppBarPosition((int)SidebarWidth);
                        this.Topmost = false;
                    }
                }
            };
        }

        public double SidebarWidth
        {
            get => this.Width > 200 ? this.Width : (SettingsService.Current.SidebarWidth > 200 ? SettingsService.Current.SidebarWidth : 350);
            set => this.Width = value;
        }

        private bool _isResizingWidth;
        private Point _resizeStartScreenPoint;
        private double _resizeStartWidth;

        private void LeftResizeGrip_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is UIElement elem)
            {
                _isResizingWidth = true;
                _resizeStartScreenPoint = PointToScreen(e.GetPosition(this));
                _resizeStartWidth = this.Width;
                elem.CaptureMouse();
                e.Handled = true;
            }
        }

        private void LeftResizeGrip_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isResizingWidth)
            {
                Point current = PointToScreen(e.GetPosition(this));
                double delta = _resizeStartScreenPoint.X - current.X;
                double newWidth = Math.Clamp(_resizeStartWidth + delta, 280, 700);
                ApplySidebarWidth(newWidth);
                e.Handled = true;
            }
        }

        private void LeftResizeGrip_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizingWidth)
            {
                _isResizingWidth = false;
                if (sender is UIElement elem) elem.ReleaseMouseCapture();

                var config = SettingsService.Current;
                config.SidebarWidth = this.Width;
                SettingsService.Save(config);
                e.Handled = true;
            }
        }

        public void ApplySidebarWidth(double newWidth)
        {
            newWidth = Math.Clamp(newWidth, 280, 700);
            this.Width = newWidth;
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = screenWidth - newWidth;

            if (SettingsService.Current.ReserveScreenSpace && _isAppBarRegistered && !_isAnimating)
            {
                SetAppBarPosition((int)newWidth);
            }
        }

        public void PeekSidebar()
        {
            if (_isAnimating || this.Visibility == Visibility.Visible)
            {
                if (this.Visibility == Visibility.Visible)
                    _peekLeaveTimer?.Stop();
                return;
            }

            _isPeeking = true;
            _edgeHandleWnd?.Hide();

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double targetLeft = screenWidth - SidebarWidth;

            this.WindowState = WindowState.Normal;
            this.Top = 0;
            this.Height = screenHeight;
            this.Width = SidebarWidth;
            this.Left = screenWidth;
            this.Topmost = true;
            this.Show();
            if (_isNotesListDirty) RefreshNotesList();

            _isAnimating = true;
            var anim = new DoubleAnimation
            {
                From = screenWidth,
                To = targetLeft,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (s, e) =>
            {
                this.BeginAnimation(Window.LeftProperty, null);
                this.Left = targetLeft;
                _isAnimating = false;
            };

            this.BeginAnimation(Window.LeftProperty, anim);
        }

        public void ExpandSidebar(bool animate = true)
        {
            if (_isAnimating) return;

            _isPeeking = false;
            _peekLeaveTimer?.Stop();
            _edgeHandleWnd?.Hide();

            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            double targetLeft = screenWidth - SidebarWidth;

            this.WindowState = WindowState.Normal;
            this.Top = 0;
            this.Height = screenHeight;
            this.Width = SidebarWidth;

            if (!animate)
            {
                this.Left = targetLeft;
                this.Show();
                if (_isNotesListDirty) RefreshNotesList();
                if (SettingsService.Current.ReserveScreenSpace)
                {
                    RegisterAppBar();
                    SetAppBarPosition((int)SidebarWidth);
                    this.Topmost = false;
                }
                else
                {
                    this.Topmost = true;
                }
                this.Activate();
                return;
            }

            _isAnimating = true;
            this.Left = screenWidth;
            this.Topmost = true;
            this.Show();
            if (_isNotesListDirty) RefreshNotesList();

            var anim = new DoubleAnimation
            {
                From = screenWidth,
                To = targetLeft,
                Duration = TimeSpan.FromMilliseconds(220),
                EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (s, e) =>
            {
                this.BeginAnimation(Window.LeftProperty, null);
                this.Left = targetLeft;
                _isAnimating = false;

                if (SettingsService.Current.ReserveScreenSpace)
                {
                    RegisterAppBar();
                    SetAppBarPosition((int)SidebarWidth);
                    this.Topmost = false;
                }
                else
                {
                    this.Topmost = true;
                }
                this.Activate();
            };

            this.BeginAnimation(Window.LeftProperty, anim);
        }

        public void CollapseSidebar(bool animate = true)
        {
            if (_isAnimating) return;

            _isPeeking = false;
            _peekLeaveTimer?.Stop();

            if (_isAppBarRegistered)
            {
                UnregisterAppBar();
            }

            double screenWidth = SystemParameters.PrimaryScreenWidth;

            if (!animate)
            {
                this.Hide();
                ShowEdgeHandle();
                return;
            }

            _isAnimating = true;
            this.Topmost = true;

            var anim = new DoubleAnimation
            {
                From = this.Left,
                To = screenWidth,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            anim.Completed += (s, e) =>
            {
                this.BeginAnimation(Window.LeftProperty, null);
                this.Hide();
                _isAnimating = false;
                ShowEdgeHandle();
            };

            this.BeginAnimation(Window.LeftProperty, anim);
        }

        public void ShowEdgeHandle()
        {
            if (_edgeHandleWnd == null)
            {
                _edgeHandleWnd = new EdgeHandleWindow(this);
            }
            _edgeHandleWnd.PositionOnScreen();
            _edgeHandleWnd.Show();
        }

        public void HideEdgeHandle()
        {
            _edgeHandleWnd?.Hide();
        }

        public void MinimizeToTray()
        {
            if (_isAppBarRegistered)
            {
                UnregisterAppBar();
            }
            this.Hide();
            _edgeHandleWnd?.Hide();
        }

        public void SnapToRightEdge()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            this.Left = screenWidth - SidebarWidth;
            this.Top = 0;
            this.Width = SidebarWidth;
            this.Height = screenHeight;
            this.Topmost = true;
        }

        public void OpenSettingsWindow()
        {
            var settingsWnd = new SettingsWindow { Owner = this };
            settingsWnd.ShowDialog();
            LoadSavedOpacity();

            if (this.Visibility == Visibility.Visible)
            {
                if (SettingsService.Current.ReserveScreenSpace && !_isAppBarRegistered)
                {
                    RegisterAppBar();
                    SetAppBarPosition((int)SidebarWidth);
                    this.Topmost = false;
                }
                else if (!SettingsService.Current.ReserveScreenSpace && _isAppBarRegistered)
                {
                    UnregisterAppBar();
                    SnapToRightEdge();
                }
            }
        }

        private void CollapseButton_Click(object sender, RoutedEventArgs e)
        {
            CollapseSidebar(animate: true);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (SearchTextBox != null && !string.IsNullOrEmpty(SearchTextBox.Text))
                {
                    SearchTextBox.Text = "";
                }
                else
                {
                    CollapseSidebar(animate: true);
                }
                e.Handled = true;
            }
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            if (SettingsService.Current.AutoCollapse && this.Visibility == Visibility.Visible && !_isAnimating)
            {
                if (_openNoteWindows.Values.Any(w => w.IsActive) ||
                    (_noteManagerWnd != null && _noteManagerWnd.IsActive) ||
                    (_graphWnd != null && _graphWnd.IsActive) ||
                    (_edgeHandleWnd != null && _edgeHandleWnd.IsActive))
                {
                    return;
                }
                CollapseSidebar(animate: true);
            }
        }
    }
}
