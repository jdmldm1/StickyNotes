using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
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
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace StickyNotes__
{
    public partial class NoteWindow : Window
    {
        private bool _isMarkdownPreviewActive = false;
        private void PreviewToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isMarkdownPreviewActive = !_isMarkdownPreviewActive;

            if (_isMarkdownPreviewActive)
            {
                PreviewToggleButton.Content = "✏";
                PreviewToggleButton.ToolTip = "Edit Note";

                EditorScrollViewer.Visibility = Visibility.Collapsed;
                MarkdownPreviewViewer.Visibility = Visibility.Visible;

                RenderMarkdownPreview();
            }
            else
            {
                PreviewToggleButton.Content = "👁";
                PreviewToggleButton.ToolTip = "Toggle Markdown Preview";

                EditorScrollViewer.Visibility = Visibility.Visible;
                MarkdownPreviewViewer.Visibility = Visibility.Collapsed;
            }
        }
        private NoteHistoryEntry? _selectedHistoryEntry;
        private Border? _selectedHistoryCardBorder;
        private bool _isFullTextView = false;

        private void TimeMachineButton_Click(object sender, RoutedEventArgs e)
        {
            if (TimeMachinePanel.Visibility == Visibility.Visible)
            {
                CloseTimeMachinePanel();
            }
            else
            {
                AiChatPanel.Visibility = Visibility.Collapsed;
                BacklinksPanel.Visibility = Visibility.Collapsed;
                TimeMachinePanel.Visibility = Visibility.Visible;
                LoadVersionHistory();
            }
        }

        private void CloseTimeMachine_Click(object sender, RoutedEventArgs e)
        {
            CloseTimeMachinePanel();
        }

        private void CloseTimeMachinePanel()
        {
            TimeMachinePanel.Visibility = Visibility.Collapsed;
            _selectedHistoryEntry = null;
            _selectedHistoryCardBorder = null;
        }

        private void LoadVersionHistory()
        {
            HistoryVersionsPanel.Children.Clear();
            DiffLinesPanel.Children.Clear();
            _selectedHistoryEntry = null;
            _selectedHistoryCardBorder = null;

            var history = DatabaseHelper.GetNoteHistory(_noteId);
            TextRange currentRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string currentPlain = currentRange.Text.Trim();

            if (history.Count == 0)
            {
                TimeMachineEmptyState.Visibility = Visibility.Visible;
                DiffScrollViewer.Visibility = Visibility.Collapsed;
                FullContentScrollViewer.Visibility = Visibility.Collapsed;
                TimeMachineVersionCountText.Text = "0 versions";
                TimeMachineRestoreBtn.IsEnabled = false;
                TimeMachineCopyBtn.IsEnabled = false;
                TimeMachineViewToggleBtn.IsEnabled = false;
                DiffRevisionTimeText.Text = "No versions saved yet";
                DiffWordDeltaText.Text = "";
                DiffStatsText.Text = "";
                return;
            }

            TimeMachineEmptyState.Visibility = Visibility.Collapsed;
            TimeMachineRestoreBtn.IsEnabled = true;
            TimeMachineCopyBtn.IsEnabled = true;
            TimeMachineViewToggleBtn.IsEnabled = true;
            UpdateTimeMachineViewMode();

            TimeMachineVersionCountText.Text = $"{history.Count} version{(history.Count == 1 ? "" : "s")}";

            Border? firstCard = null;

            foreach (var entry in history)
            {
                string histPlain = NoteContentHelper.ExtractPlainText(entry.Content);
                int histWords = NoteDiffHelper.CountWords(histPlain);
                int currentWords = NoteDiffHelper.CountWords(currentPlain);
                int wordDelta = currentWords - histWords;

                var cardBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x35)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(5),
                    Padding = new Thickness(7, 5, 7, 5),
                    Margin = new Thickness(0, 0, 0, 5),
                    Cursor = Cursors.Hand,
                    ToolTip = entry.VersionedAt.ToString("F")
                };

                var cardStack = new StackPanel();

                var timeText = new TextBlock
                {
                    Text = NoteDiffHelper.FormatRelativeTime(entry.VersionedAt),
                    Foreground = Brushes.White,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold
                };
                cardStack.Children.Add(timeText);

                var deltaRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };

                string deltaStr = wordDelta > 0 ? $"+{wordDelta}w" : wordDelta < 0 ? $"{wordDelta}w" : "= len";
                Color deltaColor = wordDelta > 0 ? Color.FromRgb(0x7E, 0xE7, 0x87) : wordDelta < 0 ? Color.FromRgb(0xFF, 0xA1, 0x98) : Color.FromRgb(0x88, 0x88, 0x92);

                var deltaText = new TextBlock
                {
                    Text = deltaStr,
                    Foreground = new SolidColorBrush(deltaColor),
                    FontSize = 9.5,
                    FontWeight = FontWeights.SemiBold
                };
                deltaRow.Children.Add(deltaText);

                var countText = new TextBlock
                {
                    Text = $" · {histWords}w",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x71, 0x71, 0x7A)),
                    FontSize = 9.5
                };
                deltaRow.Children.Add(countText);

                cardStack.Children.Add(deltaRow);
                cardBorder.Child = cardStack;

                var capturedEntry = entry;
                var capturedBorder = cardBorder;
                cardBorder.MouseLeftButtonDown += (s, args) =>
                {
                    SelectHistoryVersion(capturedEntry, capturedBorder, currentPlain);
                };

                HistoryVersionsPanel.Children.Add(cardBorder);

                if (firstCard == null) firstCard = cardBorder;
            }

            if (firstCard != null && history.Count > 0)
            {
                SelectHistoryVersion(history[0], firstCard, currentPlain);
            }
        }

        private void SelectHistoryVersion(NoteHistoryEntry entry, Border cardBorder, string currentPlain)
        {
            if (_selectedHistoryCardBorder != null)
            {
                _selectedHistoryCardBorder.Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x24));
                _selectedHistoryCardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x35));
            }

            _selectedHistoryCardBorder = cardBorder;
            _selectedHistoryCardBorder.Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x2D, 0x46));
            _selectedHistoryCardBorder.BorderBrush = new SolidColorBrush(Color.FromRgb(0x00, 0x84, 0xFF));

            _selectedHistoryEntry = entry;

            string histPlain = NoteContentHelper.ExtractPlainText(entry.Content);
            FullContentTextBox.Text = histPlain;

            var diff = NoteDiffHelper.ComputeDiff(histPlain, currentPlain);

            DiffRevisionTimeText.Text = $"{NoteDiffHelper.FormatRelativeTime(entry.VersionedAt)} · {entry.VersionedAt:g}";
            DiffRevisionTimeText.ToolTip = entry.VersionedAt.ToString("F");

            if (diff.WordDelta > 0)
            {
                DiffWordDeltaText.Text = $"+{diff.WordDelta} words in current";
                DiffWordDeltaText.Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xE7, 0x87));
            }
            else if (diff.WordDelta < 0)
            {
                DiffWordDeltaText.Text = $"{diff.WordDelta} words in current";
                DiffWordDeltaText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA1, 0x98));
            }
            else
            {
                DiffWordDeltaText.Text = "Same word count";
                DiffWordDeltaText.Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0xA6));
            }

            DiffStatsText.Text = $"+{diff.AddedCount} / -{diff.DeletedCount} lines";

            DiffLinesPanel.Children.Clear();
            foreach (var line in diff.Lines)
            {
                var rowBorder = new Border
                {
                    Padding = new Thickness(6, 1.5, 6, 1.5),
                    Margin = new Thickness(0, 0, 0, 1),
                    CornerRadius = new CornerRadius(2)
                };

                var rowGrid = new Grid();
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18, GridUnitType.Pixel) });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var indicatorText = new TextBlock
                {
                    FontFamily = new FontFamily("Consolas, Segoe UI Variable Text"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    VerticalAlignment = VerticalAlignment.Top
                };

                var contentText = new TextBlock
                {
                    Text = string.IsNullOrEmpty(line.Text) ? " " : line.Text,
                    FontFamily = new FontFamily("Consolas, Segoe UI Variable Text"),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Top
                };

                switch (line.Type)
                {
                    case DiffLineType.Added:
                        rowBorder.Background = new SolidColorBrush(Color.FromArgb(50, 46, 160, 67));
                        indicatorText.Text = "+";
                        indicatorText.Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xE7, 0x87));
                        contentText.Foreground = new SolidColorBrush(Color.FromRgb(0x7E, 0xE7, 0x87));
                        break;

                    case DiffLineType.Deleted:
                        rowBorder.Background = new SolidColorBrush(Color.FromArgb(60, 218, 54, 51));
                        indicatorText.Text = "-";
                        indicatorText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA1, 0x98));
                        contentText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0xA1, 0x98));
                        break;

                    case DiffLineType.Unchanged:
                    default:
                        rowBorder.Background = Brushes.Transparent;
                        indicatorText.Text = " ";
                        indicatorText.Foreground = Brushes.Transparent;
                        contentText.Foreground = new SolidColorBrush(Color.FromRgb(0xAA, 0xAA, 0xB2));
                        break;
                }

                Grid.SetColumn(indicatorText, 0);
                Grid.SetColumn(contentText, 1);
                rowGrid.Children.Add(indicatorText);
                rowGrid.Children.Add(contentText);
                rowBorder.Child = rowGrid;

                DiffLinesPanel.Children.Add(rowBorder);
            }
        }

        private void TimeMachineViewToggle_Click(object sender, RoutedEventArgs e)
        {
            _isFullTextView = !_isFullTextView;
            UpdateTimeMachineViewMode();
        }

        private void UpdateTimeMachineViewMode()
        {
            if (_isFullTextView)
            {
                DiffScrollViewer.Visibility = Visibility.Collapsed;
                FullContentScrollViewer.Visibility = Visibility.Visible;
                TimeMachineViewToggleBtn.Content = "Show Diff";
            }
            else
            {
                DiffScrollViewer.Visibility = Visibility.Visible;
                FullContentScrollViewer.Visibility = Visibility.Collapsed;
                TimeMachineViewToggleBtn.Content = "Full Text";
            }
        }

        private void TimeMachineCopy_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHistoryEntry == null) return;

            string text = NoteContentHelper.ExtractPlainText(_selectedHistoryEntry.Content);
            try
            {
                Clipboard.SetText(text);
                TimeMachineCopyBtn.Content = "✓ Copied";
                var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                timer.Tick += (s, args) =>
                {
                    TimeMachineCopyBtn.Content = "📋 Copy";
                    timer.Stop();
                };
                timer.Start();
            }
            catch { }
        }

        private void TimeMachineRestore_Click(object sender, RoutedEventArgs e)
        {
            if (_selectedHistoryEntry == null) return;

            string relTime = NoteDiffHelper.FormatRelativeTime(_selectedHistoryEntry.VersionedAt);
            var confirm = MessageBox.Show(
                $"Restore note to revision from {relTime} ({_selectedHistoryEntry.VersionedAt:g})?\n\nA backup of your current note will be preserved in version history automatically.",
                "Restore Revision",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            TextRange currentRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string backupXaml = NoteContentHelper.SaveRange(currentRange);
            DatabaseHelper.AddNoteHistoryEntry(_note.Id, backupXaml);

            if (NoteContentHelper.TryLoadRange(currentRange, _selectedHistoryEntry.Content))
            {
                RewireInteractiveElements();
                SaveNoteContent();
                CloseTimeMachinePanel();
                MessageBox.Show(
                    "Version restored successfully!\nA backup of your previous text has been saved in version history.",
                    "Version Restored",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        private void RenderMarkdownPreview()
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string plainText = range.Text;
            MarkdownPreviewViewer.Document = MarkdownHelper.Parse(plainText, TaskChangedHandler);
        }
        private void TaskChangedHandler(int lineIndex, bool isChecked)
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string rawText = range.Text;

            string[] lines = rawText.Split(new[] { "\r\n", "\r", "\n" }, System.StringSplitOptions.None);
            if (lineIndex >= 0 && lineIndex < lines.Length)
            {
                string line = lines[lineIndex];
                if (isChecked)
                {
                    if (line.Contains("[ ]")) line = line.Replace("[ ]", "[x]");
                    else if (line.Contains("[]")) line = line.Replace("[]", "[x]");
                }
                else
                {
                    if (line.Contains("[x]")) line = line.Replace("[x]", "[ ]");
                }
                lines[lineIndex] = line;

                string updatedText = string.Join(System.Environment.NewLine, lines);

                NoteRichTextBox.Document.Blocks.Clear();
                NoteRichTextBox.Document.Blocks.Add(new Paragraph(new Run(updatedText)));

                SaveNoteContent();
                RenderMarkdownPreview();
            }
        }
    }
}
