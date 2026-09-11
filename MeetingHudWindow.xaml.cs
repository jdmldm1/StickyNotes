using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StickyNotes__
{
    public partial class MeetingHudWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private int _noteId;
        private TeamsMeetingData _meetingData;
        private readonly Stopwatch _stopwatch = new();
        private readonly DispatcherTimer _timer = new();
        private bool _isCuiActive = false;
        private readonly HashSet<string> _presentAttendees = new(StringComparer.OrdinalIgnoreCase);

        public MeetingHudWindow(MainWindow mainWindow, int noteId, TeamsMeetingData? meetingData = null)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            _noteId = noteId;
            _meetingData = meetingData ?? new TeamsMeetingData { Title = "Active Meeting" };

            HudTitleText.Text = _meetingData.Title;
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) =>
            {
                TimerDisplay.Text = _stopwatch.Elapsed.ToString(@"hh\:mm\:ss");
            };

            PopulateRollCall();
            PositionWindow();
        }

        private void PositionWindow()
        {
            double screenW = SystemParameters.PrimaryScreenWidth;
            Left = screenW - Width - 30;
            Top = 60;
        }

        private void PopulateRollCall()
        {
            AttendeesRollCallPanel.Children.Clear();
            if (_meetingData.Attendees.Count == 0)
            {
                var pill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 4, 0),
                    Child = new TextBlock { Text = "👤 No roster", Foreground = Brushes.Gray, FontSize = 9.5 }
                };
                AttendeesRollCallPanel.Children.Add(pill);
                return;
            }

            foreach (var att in _meetingData.Attendees)
            {
                var pill = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(30, 0, 132, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 132, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 4, 2),
                    Cursor = Cursors.Hand,
                    Tag = att
                };
                var tb = new TextBlock { Text = "👤 " + att, Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0xD0, 0xFF)), FontSize = 9.5 };
                pill.Child = tb;

                pill.MouseLeftButtonDown += (s, e) =>
                {
                    if (_presentAttendees.Contains(att))
                    {
                        _presentAttendees.Remove(att);
                        pill.Background = new SolidColorBrush(Color.FromArgb(30, 0, 132, 255));
                        pill.BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0, 132, 255));
                        tb.Text = "👤 " + att;
                        tb.Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0xD0, 0xFF));
                    }
                    else
                    {
                        _presentAttendees.Add(att);
                        pill.Background = new SolidColorBrush(Color.FromArgb(50, 40, 180, 70));
                        pill.BorderBrush = new SolidColorBrush(Color.FromArgb(120, 40, 180, 70));
                        tb.Text = "✓ " + att;
                        tb.Foreground = new SolidColorBrush(Color.FromRgb(0xA7, 0xF3, 0xD0));
                    }
                };

                AttendeesRollCallPanel.Children.Add(pill);
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _timer.Stop();
            _stopwatch.Stop();
            Close();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            PinButton.Foreground = Topmost ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) : Brushes.Gray;
        }

        private void TimerPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                _timer.Stop();
                TimerPlayPauseBtn.Content = "▶";
            }
            else
            {
                _stopwatch.Start();
                _timer.Start();
                TimerPlayPauseBtn.Content = "⏸";
            }
        }

        private void TimerReset_Click(object sender, RoutedEventArgs e)
        {
            _stopwatch.Reset();
            TimerDisplay.Text = "00:00:00";
            TimerPlayPauseBtn.Content = "▶";
        }

        private void QuickInputBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                CommitQuickCapture();
                e.Handled = true;
            }
        }

        private void QuickAdd_Click(object sender, RoutedEventArgs e)
        {
            CommitQuickCapture();
        }

        private void CommitQuickCapture()
        {
            string raw = QuickInputBox.Text.Trim();
            if (string.IsNullOrEmpty(raw)) return;

            EmptyNoticeText.Visibility = Visibility.Collapsed;
            string timestamp = _stopwatch.Elapsed.ToString(@"hh\:mm");
            string formattedItem = $"[{timestamp}] {raw}";

            var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var check = new CheckBox
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            var tb = new TextBlock
            {
                Text = formattedItem,
                Foreground = Brushes.White,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            check.Checked += (s, e) => tb.TextDecorations = TextDecorations.Strikethrough;
            check.Unchecked += (s, e) => tb.TextDecorations = null;

            Grid.SetColumn(check, 0);
            Grid.SetColumn(tb, 1);
            row.Children.Add(check);
            row.Children.Add(tb);

            ItemsPanel.Children.Add(row);

            _meetingData.ActionItems.Add(formattedItem);

            try
            {
                var note = DatabaseHelper.GetNote(_noteId);
                if (note != null)
                {
                    string plain = NoteContentHelper.ExtractPlainText(note.Content);
                    plain += $"\n- [ ] {formattedItem}";
                    note.Content = NoteContentHelper.BuildContentFromPlainText(plain);
                    DatabaseHelper.UpdateNote(note);
                }
            }
            catch { }

            QuickInputBox.Text = "";
            QuickInputBox.Focus();
        }

        private void CopyBluf_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string bluf = TeamsIntegrationHelper.GenerateBlufBrief(_meetingData);
                Clipboard.SetText(bluf);
                if (sender is Button btn)
                {
                    string original = btn.Content.ToString() ?? "📋 Copy BLUF Brief";
                    btn.Content = "✓ Copied!";
                    var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    resetTimer.Tick += (s, args) =>
                    {
                        resetTimer.Stop();
                        btn.Content = original;
                    };
                    resetTimer.Start();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to copy BLUF: " + ex.Message, "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ToggleCui_Click(object sender, RoutedEventArgs e)
        {
            _isCuiActive = !_isCuiActive;
            if (_isCuiActive)
            {
                MainHudBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x93, 0x33, 0xEA));
                MainHudBorder.BorderThickness = new Thickness(2);
                CuiBadgeBtn.Background = new SolidColorBrush(Color.FromRgb(0x4C, 0x1D, 0x95));
                CuiBadgeBtn.Content = "🛡️ CUI Active";
                _meetingData.IsDoD = true;
            }
            else
            {
                MainHudBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x84, 0xFF));
                MainHudBorder.BorderThickness = new Thickness(1.5);
                CuiBadgeBtn.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x10, 0x65));
                CuiBadgeBtn.Content = "🛡️ CUI Mode";
            }
        }

        private void OpenFullNote_Click(object sender, RoutedEventArgs e)
        {
            _mainWindow.OpenNoteWindow(_noteId);
        }
    }
}
