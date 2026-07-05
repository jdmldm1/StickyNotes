using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StickyNotes__
{
    // A single-line, no-window-management capture box: press the hotkey, type a thought,
    // hit Enter, it's filed as a note instantly. No note window ever has to be positioned,
    // found, or closed for a quick thought to get captured.
    public partial class QuickCaptureWindow : Window
    {
        private readonly MainWindow _mainWnd;
        private readonly DispatcherTimer _closeTimer;

        public QuickCaptureWindow(MainWindow mainWnd)
        {
            InitializeComponent();
            _mainWnd = mainWnd;
            _closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            _closeTimer.Tick += (s, e) => { _closeTimer.Stop(); this.Hide(); };
        }

        public void FocusCapture()
        {
            CaptureInput.Text = "";
            CaptureInput.IsEnabled = true;
            StatusIcon.Text = "✏";
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0x84, 0xff));
            CaptureInput.Focus();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FocusCapture();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Hide();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                SaveAndClose();
                e.Handled = true;
            }
        }

        private void SaveAndClose()
        {
            string text = CaptureInput.Text.Trim();
            if (string.IsNullOrEmpty(text))
            {
                this.Hide();
                return;
            }

            string title = text.Length > 40 ? text.Substring(0, 40) + "..." : text;
            string contentXaml = BuildPlainNoteXaml(text);

            int noteId = DatabaseHelper.CreateNote(title, contentXaml, null, null, "yellow");
            _mainWnd.RefreshNotesList();
            _mainWnd.RefreshTagsFilter();

            // Flash a brief confirmation instead of just vanishing, so it's clear the note
            // was actually saved even though no note window ever appeared.
            CaptureInput.IsEnabled = false;
            CaptureInput.Text = "Saved!";
            StatusIcon.Text = "✓";
            StatusIcon.Foreground = new SolidColorBrush(Color.FromRgb(0x4c, 0xd9, 0x64));
            _closeTimer.Start();
        }

        private static string BuildPlainNoteXaml(string text)
        {
            var paragraph = new Paragraph(new Run(text));
            var document = new FlowDocument(paragraph)
            {
                // Standalone FlowDocument -- see BuildMeetingNoteXaml in MainWindow.xaml.cs for why
                // Foreground/FontFamily must be set explicitly here.
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif")
            };

            var range = new TextRange(document.ContentStart, document.ContentEnd);
            return NoteContentHelper.SaveRange(range);
        }
    }
}
