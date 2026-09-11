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
        private void InsertHyperlink_Click(object sender, RoutedEventArgs e)
        {
            string selectedText = NoteRichTextBox.Selection.Text;

            var dialog = new InputDialog("Enter the URL:", "Insert Hyperlink", "https://") { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            string url = dialog.Answer.Trim();
            if (!Regex.IsMatch(url, @"^[a-zA-Z][a-zA-Z\d+\-.]*://"))
                url = "https://" + url;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                MessageBox.Show("That doesn't look like a valid URL.", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string displayText = string.IsNullOrWhiteSpace(selectedText) ? url : selectedText;

            if (!NoteRichTextBox.Selection.IsEmpty)
                NoteRichTextBox.Selection.Text = string.Empty;

            var paragraph = NoteRichTextBox.CaretPosition.Paragraph;
            if (paragraph == null)
            {
                paragraph = new Paragraph();
                NoteRichTextBox.Document.Blocks.Add(paragraph);
            }

            var hyperlink = new Hyperlink(new Run(displayText))
            {
                NavigateUri = uri,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4d, 0xb8, 0xff))
            };
            hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
            paragraph.Inlines.Add(hyperlink);

            NoteRichTextBox.Focus();
        }
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            OpenUrl(e.Uri);
            e.Handled = true;
        }
        private static void OpenUrl(Uri uri)
        {
            try
            {
                if (SecurityHelper.IsSafeWebUri(uri))
                {
                    Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
                }
            }
            catch { }
        }
        private void NoteRichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var position = NoteRichTextBox.GetPositionFromPoint(e.GetPosition(NoteRichTextBox), true);

            if (Keyboard.Modifiers == ModifierKeys.Control && position != null)
            {
                var hyperlink = FindAncestorHyperlink(position.Parent);
                if (hyperlink?.NavigateUri != null)
                {
                    if (hyperlink.NavigateUri.Scheme == "note" && int.TryParse(hyperlink.NavigateUri.Host, out int targetNoteId))
                    {
                        var main = Application.Current.MainWindow as MainWindow;
                        main?.OpenNoteWindow(targetNoteId);
                        e.Handled = true;
                        return;
                    }

                    OpenUrl(hyperlink.NavigateUri);
                    e.Handled = true;
                    return;
                }

                var para = position.Paragraph;
                if (para != null)
                {
                    string paraText = new TextRange(para.ContentStart, para.ContentEnd).Text;
                    var matches = Regex.Matches(paraText, @"\[\[([^\]]+)\]\]");
                    int clickOffset = new TextRange(para.ContentStart, position).Text.Length;
                    foreach (Match m in matches)
                    {
                        if (clickOffset >= m.Index && clickOffset <= m.Index + m.Length)
                        {
                            string targetTitle = m.Groups[1].Value.Trim();
                            var targetNote = DatabaseHelper.GetNoteByTitle(targetTitle);
                            if (targetNote != null)
                            {
                                var main = Application.Current.MainWindow as MainWindow;
                                main?.OpenNoteWindow(targetNote.Id);
                                e.Handled = true;
                                return;
                            }
                        }
                    }
                }
            }

            var checklistRun = GetChecklistRunAt(position);
            if (checklistRun != null)
            {
                ToggleChecklistItem(checklistRun);
                e.Handled = true;
            }
        }
        private static Hyperlink? FindAncestorHyperlink(DependencyObject? element)
        {
            while (element != null)
            {
                if (element is Hyperlink hyperlink) return hyperlink;
                element = LogicalTreeHelper.GetParent(element);
            }
            return null;
        }
        private void NoteRichTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (WikilinkPopup != null && WikilinkPopup.IsOpen)
            {
                if (e.Key == Key.Down)
                {
                    if (WikilinkListBox.SelectedIndex < WikilinkListBox.Items.Count - 1)
                    {
                        WikilinkListBox.SelectedIndex++;
                        WikilinkListBox.ScrollIntoView(WikilinkListBox.SelectedItem);
                    }
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Up)
                {
                    if (WikilinkListBox.SelectedIndex > 0)
                    {
                        WikilinkListBox.SelectedIndex--;
                        WikilinkListBox.ScrollIntoView(WikilinkListBox.SelectedItem);
                    }
                    e.Handled = true;
                    return;
                }
                if (e.Key == Key.Enter || e.Key == Key.Tab)
                {
                    if (WikilinkListBox.SelectedItem is Note selected)
                    {
                        InsertSelectedWikilink(selected);
                        e.Handled = true;
                        return;
                    }
                }
                if (e.Key == Key.Escape)
                {
                    WikilinkPopup.IsOpen = false;
                    e.Handled = true;
                    return;
                }
            }

            if (e.Key != Key.Enter) return;

            var paragraph = NoteRichTextBox.CaretPosition.Paragraph;
            var checklistRun = GetChecklistRun(paragraph);
            if (checklistRun == null || paragraph == null) return;

            e.Handled = true;

            var newParagraph = new Paragraph();
            var newRun = new Run(UncheckedGlyph);
            newParagraph.Inlines.Add(newRun);

            NoteRichTextBox.Document.Blocks.InsertAfter(paragraph, newParagraph);
            NoteRichTextBox.CaretPosition = newRun.ContentEnd;
        }

        public void CheckWikilinkTrigger()
        {
            var caret = NoteRichTextBox.CaretPosition;
            var paragraph = caret?.Paragraph;
            if (paragraph == null)
            {
                if (WikilinkPopup != null && WikilinkPopup.IsOpen) WikilinkPopup.IsOpen = false;
                return;
            }

            var range = new TextRange(paragraph.ContentStart, caret);
            string lineText = range.Text;
            int bracketIdx = lineText.LastIndexOf("[[", StringComparison.Ordinal);
            if (bracketIdx >= 0)
            {
                int closeIdx = lineText.IndexOf("]]", bracketIdx, StringComparison.Ordinal);
                if (closeIdx == -1)
                {
                    string query = lineText.Substring(bracketIdx + 2).Trim();
                    ShowWikilinkAutocomplete(query);
                    return;
                }
            }

            if (WikilinkPopup != null && WikilinkPopup.IsOpen)
            {
                WikilinkPopup.IsOpen = false;
            }
        }

        private void ShowWikilinkAutocomplete(string query)
        {
            try
            {
                var allNotes = DatabaseHelper.ListNotes();
                var filtered = allNotes
                    .Where(n => n.Id != _noteId && !string.IsNullOrWhiteSpace(n.Title))
                    .Where(n => string.IsNullOrWhiteSpace(query) || n.Title.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                    .OrderBy(n => n.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                    .ThenBy(n => n.Title)
                    .Take(12)
                    .ToList();

                if (filtered.Count > 0)
                {
                    WikilinkListBox.ItemsSource = filtered;
                    WikilinkListBox.SelectedIndex = 0;

                    Rect caretRect = NoteRichTextBox.CaretPosition.GetCharacterRect(LogicalDirection.Forward);
                    WikilinkPopup.HorizontalOffset = Math.Max(10, Math.Min(caretRect.Left, this.Width - 280));
                    WikilinkPopup.VerticalOffset = caretRect.Bottom + 4;
                    WikilinkPopup.IsOpen = true;
                }
                else
                {
                    WikilinkPopup.IsOpen = false;
                }
            }
            catch
            {
                WikilinkPopup.IsOpen = false;
            }
        }

        private void InsertSelectedWikilink(Note selectedNote)
        {
            try
            {
                var caret = NoteRichTextBox.CaretPosition;
                var paragraph = caret.Paragraph;
                if (paragraph != null)
                {
                    var range = new TextRange(paragraph.ContentStart, caret);
                    string lineText = range.Text;
                    int bracketIdx = lineText.LastIndexOf("[[", StringComparison.Ordinal);
                    if (bracketIdx >= 0)
                    {
                        TextPointer? startPointer = paragraph.ContentStart.GetPositionAtOffset(bracketIdx);
                        if (startPointer != null)
                        {
                            var replaceRange = new TextRange(startPointer, caret);
                            replaceRange.Text = $"[[{selectedNote.Title}]] ";
                            NoteRichTextBox.CaretPosition = replaceRange.End;
                        }
                    }
                }

                DatabaseHelper.AddNoteConnection(_noteId, selectedNote.Id);
                RefreshBacklinksPanel();
                SaveNoteContent();
            }
            catch { }
            finally
            {
                WikilinkPopup.IsOpen = false;
                NoteRichTextBox.Focus();
            }
        }

        private void WikilinkListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (WikilinkListBox.SelectedItem is Note selected)
            {
                InsertSelectedWikilink(selected);
            }
        }

        private void WikilinkListBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter && WikilinkListBox.SelectedItem is Note selected)
            {
                InsertSelectedWikilink(selected);
                e.Handled = true;
            }
        }
        private void AutoDetectUrl(TextChangedEventArgs e)
        {
            if (e.Changes.Count != 1) return;
            var change = e.Changes.First();
            if (change.AddedLength != 1 || change.RemovedLength != 0) return;

            var caret = NoteRichTextBox.CaretPosition;
            var run = caret.GetAdjacentElement(LogicalDirection.Backward) as Run
                      ?? caret.Parent as Run;
            if (run == null || run.Parent is Hyperlink) return;

            string text = run.Text;
            if (string.IsNullOrEmpty(text)) return;

            char lastChar = text[text.Length - 1];
            if (lastChar != ' ' && lastChar != '\t' && lastChar != '\n') return;

            string beforeTrigger = text.Substring(0, text.Length - 1);
            int lastBreak = beforeTrigger.LastIndexOfAny(new[] { ' ', '\t', '\n' });
            string candidate = lastBreak >= 0 ? beforeTrigger.Substring(lastBreak + 1) : beforeTrigger;
            if (string.IsNullOrEmpty(candidate) || !UrlRegex.IsMatch(candidate)) return;

            string normalizedUrl = candidate.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? candidate
                : "https://" + candidate;
            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var uri)) return;

            try
            {
                _isAutoFormatting = true;

                int urlStartOffset = text.Length - 1 - candidate.Length;
                TextPointer? urlStart = run.ContentStart.GetPositionAtOffset(urlStartOffset);
                TextPointer? urlEnd = urlStart?.GetPositionAtOffset(candidate.Length);
                if (urlStart == null || urlEnd == null) return;

                var range = new TextRange(urlStart, urlEnd);
                range.Text = string.Empty;

                var hyperlink = new Hyperlink(new Run(candidate), urlStart)
                {
                    NavigateUri = uri,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x4d, 0xb8, 0xff))
                };
                hyperlink.RequestNavigate += Hyperlink_RequestNavigate;

                var caretAfter = hyperlink.ElementEnd.GetPositionAtOffset(1);
                NoteRichTextBox.CaretPosition = caretAfter ?? hyperlink.ElementEnd;
            }
            catch
            {
            }
            finally
            {
                _isAutoFormatting = false;
            }
        }
        private void RewireInteractiveElements()
        {
            RewireBlocks(NoteRichTextBox.Document.Blocks);
        }
        private void RewireBlocks(BlockCollection blocks)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        RewireInlines(paragraph.Inlines);
                        break;
                    case List list:
                        foreach (var item in list.ListItems)
                            RewireBlocks(item.Blocks);
                        break;
                    case Section section:
                        RewireBlocks(section.Blocks);
                        break;
                }
            }
        }
        private void RewireInlines(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Hyperlink hyperlink:
                        hyperlink.RequestNavigate -= Hyperlink_RequestNavigate;
                        hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
                        RewireInlines(hyperlink.Inlines);
                        break;
                    case Span span:
                        NormalizeForegroundIfBlack(span);
                        RewireInlines(span.Inlines);
                        break;
                    case Run run:
                        NormalizeForegroundIfBlack(run);
                        break;
                }
            }
        }
        private static void NormalizeForegroundIfBlack(Inline inline)
        {
            if (inline.Foreground is SolidColorBrush brush && brush.Color == Colors.Black)
            {
                inline.Foreground = Brushes.White;
            }
        }
    }
}
