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
        private string? _prePreviewContentXaml;
        private void TimeMachineButton_Click(object sender, RoutedEventArgs e)
        {
            if (TimeMachinePanel.Visibility == Visibility.Visible)
            {
                CloseTimeMachinePanel();
            }
            else
            {
                AiChatPanel.Visibility = Visibility.Collapsed;
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
            RestoreOriginalContentIfPreviewing();
        }
        private void RestoreOriginalContentIfPreviewing()
        {
            if (_prePreviewContentXaml != null)
            {
                TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                NoteContentHelper.TryLoadRange(range, _prePreviewContentXaml);
                TitleTextBlock.Text = string.IsNullOrEmpty(_note.Title) ? "Sticky Note" : _note.Title;
                _prePreviewContentXaml = null;
            }
        }
        private void LoadVersionHistory()
        {
            HistoryVersionsPanel.Children.Clear();
            var history = DatabaseHelper.GetNoteHistory(_noteId);

            if (history.Count == 0)
            {
                HistoryVersionsPanel.Children.Add(new TextBlock
                {
                    Text = "No previous versions saved yet. Versions are saved automatically as you make changes.",
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(5, 10, 5, 0)
                });
                return;
            }

            foreach (var entry in history)
            {
                var border = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(20, 255, 255, 255)),
                    CornerRadius = new CornerRadius(4),
                    Padding = new Thickness(8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoStack = new StackPanel();
                Grid.SetColumn(infoStack, 0);
                infoStack.Children.Add(new TextBlock
                {
                    Text = entry.VersionedAt.ToString("g"),
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 11
                });

                string plainText = NoteContentHelper.ExtractPlainText(entry.Content);
                if (plainText.Length > 40) plainText = plainText.Substring(0, 40) + "...";

                infoStack.Children.Add(new TextBlock
                {
                    Text = plainText,
                    Foreground = Brushes.LightGray,
                    FontSize = 10,
                    Margin = new Thickness(0, 2, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                grid.Children.Add(infoStack);

                var btnStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(btnStack, 1);
                
                var previewBtn = new Button
                {
                    Content = "Preview",
                    Margin = new Thickness(0, 0, 4, 0),
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                    Foreground = Brushes.White,
                    FontSize = 9.5,
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(0)
                };
                previewBtn.Click += (s, args) => PreviewVersion(entry.Content);
                btnStack.Children.Add(previewBtn);

                var restoreBtn = new Button
                {
                    Content = "Restore",
                    Padding = new Thickness(8, 2, 8, 2),
                    Background = new SolidColorBrush(Color.FromRgb(0, 132, 255)),
                    Foreground = Brushes.White,
                    FontSize = 9.5,
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(0),
                    FontWeight = FontWeights.SemiBold
                };
                restoreBtn.Click += (s, args) => RestoreVersion(entry.Content);
                btnStack.Children.Add(restoreBtn);

                grid.Children.Add(btnStack);
                border.Child = grid;

                HistoryVersionsPanel.Children.Add(border);
            }
        }
        private void PreviewVersion(string content)
        {
            if (_prePreviewContentXaml == null)
            {
                TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                _prePreviewContentXaml = NoteContentHelper.SaveRange(range);
            }

            TextRange loadRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            if (NoteContentHelper.TryLoadRange(loadRange, content))
            {
                RewireInteractiveElements();
                TitleTextBlock.Text = "Sticky Note (Previewing Version...)";
            }
        }
        private void RestoreVersion(string content)
        {
            _prePreviewContentXaml = null;
            TextRange loadRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            NoteContentHelper.TryLoadRange(loadRange, content);
            RewireInteractiveElements();

            SaveNoteContent();
            CloseTimeMachinePanel();
            MessageBox.Show("Version restored successfully!", "Version Restored", MessageBoxButton.OK, MessageBoxImage.Information);
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
