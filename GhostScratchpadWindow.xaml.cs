using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StickyNotes__
{
    public partial class GhostScratchpadWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private int _scratchpadNoteId = -1;
        private readonly DispatcherTimer _autoSaveTimer = new();
        private bool _isLoading = true;

        public GhostScratchpadWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;

            _autoSaveTimer.Interval = TimeSpan.FromMilliseconds(400);
            _autoSaveTimer.Tick += (s, e) =>
            {
                _autoSaveTimer.Stop();
                SaveScratchpadToDb();
            };

            LoadScratchpadFromDb();
            PositionOnRightEdge();

            Deactivated += (s, e) =>
            {
                if (!Topmost)
                {
                    SaveScratchpadToDb();
                    Hide();
                }
            };

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    SaveScratchpadToDb();
                    Hide();
                    e.Handled = true;
                }
            };
        }

        public void PositionOnRightEdge()
        {
            double screenW = SystemParameters.PrimaryScreenWidth;
            double screenH = SystemParameters.PrimaryScreenHeight;
            Left = screenW - Width - 15;
            Top = Math.Max(80, (screenH - Height) / 2);
        }

        public void FocusAndActivate()
        {
            PositionOnRightEdge();
            Show();
            Activate();
            ScratchInput.Focus();
            ScratchInput.CaretIndex = ScratchInput.Text.Length;
        }

        private void LoadScratchpadFromDb()
        {
            _isLoading = true;
            try
            {
                var notes = DatabaseHelper.ListNotes(includeContent: true);
                var existing = notes.FirstOrDefault(n => n.Title == "⚡ Ghost Scratchpad");
                if (existing != null)
                {
                    _scratchpadNoteId = existing.Id;
                    string plain = NoteContentHelper.ExtractPlainText(existing.Content);
                    ScratchInput.Text = plain;
                }
                else
                {
                    string initialPkg = NoteContentHelper.BuildContentFromPlainText("");
                    _scratchpadNoteId = DatabaseHelper.CreateNote("⚡ Ghost Scratchpad", initialPkg, null, null, "charcoal");
                    try { DatabaseHelper.AddTagToNote(_scratchpadNoteId, "scratchpad"); } catch { }
                }
            }
            catch { }
            finally
            {
                _isLoading = false;
                UpdateWordCount();
            }
        }

        private void SaveScratchpadToDb()
        {
            if (_isLoading || _scratchpadNoteId <= 0) return;

            try
            {
                string text = ScratchInput.Text;
                var note = DatabaseHelper.GetNote(_scratchpadNoteId);
                if (note != null)
                {
                    note.Content = NoteContentHelper.BuildContentFromPlainText(text);
                    note.PlainText = text;
                    DatabaseHelper.UpdateNote(note);
                    SavedIndicatorText.Text = " · Saved";
                    SavedIndicatorText.Foreground = new SolidColorBrush(Color.FromRgb(0x4A, 0xDE, 0x80));
                }
            }
            catch { }
        }

        private void ScratchInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            PlaceholderText.Visibility = string.IsNullOrEmpty(ScratchInput.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (_isLoading) return;

            SavedIndicatorText.Text = " · Saving...";
            SavedIndicatorText.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0x9E, 0x0B));
            _autoSaveTimer.Stop();
            _autoSaveTimer.Start();

            UpdateWordCount();
        }

        private void UpdateWordCount()
        {
            string t = ScratchInput.Text;
            int chars = t.Length;
            int words = string.IsNullOrWhiteSpace(t) ? 0 : t.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
            WordCountText.Text = $"{words} word{(words == 1 ? "" : "s")} · {chars} char{(chars == 1 ? "" : "s")}";
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            SaveScratchpadToDb();
            Hide();
        }

        private void PinButton_Click(object sender, RoutedEventArgs e)
        {
            Topmost = !Topmost;
            PinButton.Foreground = Topmost ? new SolidColorBrush(Color.FromRgb(0x38, 0xBD, 0xF8)) : Brushes.Gray;
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!string.IsNullOrEmpty(ScratchInput.Text))
                {
                    Clipboard.SetText(ScratchInput.Text);
                    SavedIndicatorText.Text = " · Copied to clipboard!";
                }
            }
            catch { }
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(ScratchInput.Text)) return;
            var res = MessageBox.Show("Clear the scratchpad?", "Clear Scratchpad", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                ScratchInput.Text = "";
                SaveScratchpadToDb();
            }
        }

        private void ConvertToNote_Click(object sender, RoutedEventArgs e)
        {
            string text = ScratchInput.Text.Trim();
            if (string.IsNullOrEmpty(text)) return;

            string firstLine = text.Split('\n')[0].Trim();
            string title = firstLine.Length > 40 ? firstLine.Substring(0, 38) + "…" : firstLine;
            if (string.IsNullOrWhiteSpace(title)) title = "Note from Scratchpad";

            string pkg = NoteContentHelper.BuildContentFromPlainText(text);
            int newNoteId = DatabaseHelper.CreateNote(title, pkg, null, null, "yellow");

            _mainWindow.RefreshNotesList();
            _mainWindow.OpenNoteWindow(newNoteId);

            ScratchInput.Text = "";
            SaveScratchpadToDb();
            Hide();
        }
    }
}
