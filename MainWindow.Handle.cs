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
                        SetAppBarPosition(350);
                        this.Topmost = false;
                    }
                }
            };
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
            double targetLeft = screenWidth - 350;

            this.WindowState = WindowState.Normal;
            this.Top = 0;
            this.Height = screenHeight;
            this.Width = 350;
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
            double targetLeft = screenWidth - 350;

            this.WindowState = WindowState.Normal;
            this.Top = 0;
            this.Height = screenHeight;
            this.Width = 350;

            if (!animate)
            {
                this.Left = targetLeft;
                this.Show();
                if (_isNotesListDirty) RefreshNotesList();
                if (SettingsService.Current.ReserveScreenSpace)
                {
                    RegisterAppBar();
                    SetAppBarPosition(350);
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
                    SetAppBarPosition(350);
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
            this.Left = screenWidth - 350;
            this.Top = 0;
            this.Width = 350;
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
                    SetAppBarPosition(350);
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
