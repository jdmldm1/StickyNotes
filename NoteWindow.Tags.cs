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
        private void AddTagButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new InputDialog("Enter tag name:", "Add Tag");
            if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Answer))
            {
                string tag = dlg.Answer.Trim().ToLower();
                if (!string.IsNullOrEmpty(tag))
                {
                    TextRange range = new TextRange(NoteRichTextBox.Document.ContentEnd, NoteRichTextBox.Document.ContentEnd);
                    range.Text = " #" + tag;
                    NoteRichTextBox.Focus();
                }
            }
        }
        private async void AiTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (!await AiHelper.IsOllamaRunningAsync())
            {
                MessageBox.Show("Please start Ollama (local AI service) to use Auto-Tag.", "AI Auto-Tag", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            AiTagButton.IsEnabled = false;
            try
            {
                TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                string text = NoteTitleTextBox.Text + "\n" + range.Text;
                var tags = await AiHelper.AutoTagTextAsync(text);

                if (tags.Count > 0)
                {
                    var existingTags = new HashSet<string>(DatabaseHelper.GetNoteTags(_noteId), StringComparer.OrdinalIgnoreCase);
                    var newTagsAdded = new List<string>();

                    foreach (var tag in tags)
                    {
                        string trimmed = tag.Trim().ToLower();
                        if (!string.IsNullOrEmpty(trimmed) && !existingTags.Contains(trimmed))
                        {
                            DatabaseHelper.AddTagToNote(_noteId, trimmed);
                            newTagsAdded.Add(trimmed);
                        }
                    }

                    if (newTagsAdded.Count > 0)
                    {
                        TextRange docRange = new TextRange(NoteRichTextBox.Document.ContentEnd, NoteRichTextBox.Document.ContentEnd);
                        docRange.Text = " " + string.Join(" ", newTagsAdded.Select(t => $"#{t}"));
                        UpdateTagsDisplay();
                        NotifyNotesChanged();
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"AI Auto-Tag failed: {ex.Message}", "AI Auto-Tag", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                AiTagButton.IsEnabled = true;
            }
        }
        private void TagsTextBlock_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var tags = DatabaseHelper.GetNoteTags(_noteId);
            var menu = new ContextMenu();

            if (tags.Count > 0)
            {
                foreach (var tag in tags)
                {
                    var tagItem = new MenuItem { Header = $"#{tag}" };

                    var removeItem = new MenuItem { Header = "Remove" };
                    removeItem.Click += (s, args) =>
                    {
                        RemoveHashtagFromDocument(tag);
                        DatabaseHelper.RemoveTagFromNote(_noteId, tag);
                        UpdateTagsDisplay();
                        NotifyNotesChanged();
                    };
                    tagItem.Items.Add(removeItem);

                    var renameItem = new MenuItem { Header = "Rename..." };
                    renameItem.Click += (s, args) =>
                    {
                        var dlg = new InputDialog("Rename tag:", "Rename Tag", tag) { Owner = this };
                        if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Answer))
                        {
                            string newTag = dlg.Answer.Trim().ToLower();
                            RenameHashtagInDocument(tag, newTag);
                            DatabaseHelper.RemoveTagFromNote(_noteId, tag);
                            DatabaseHelper.AddTagToNote(_noteId, newTag);
                            UpdateTagsDisplay();
                            NotifyNotesChanged();
                        }
                    };
                    tagItem.Items.Add(renameItem);

                    menu.Items.Add(tagItem);
                }
                menu.Items.Add(new Separator());
            }

            var addTagItem = new MenuItem { Header = "+ Add Tag..." };
            addTagItem.Click += (s, args) => AddTagButton_Click(sender, e);
            menu.Items.Add(addTagItem);

            menu.PlacementTarget = TagsTextBlock;
            menu.IsOpen = true;
            e.Handled = true;
        }
        private void RemoveHashtagFromDocument(string tag)
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string docText = range.Text;
            string target = "#" + tag;
            int idx = docText.IndexOf(target, StringComparison.OrdinalIgnoreCase);
            if (idx != -1)
            {
                for (TextPointer p = NoteRichTextBox.Document.ContentStart; p != null && p.CompareTo(NoteRichTextBox.Document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
                {
                    if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                    {
                        string text = p.GetTextInRun(LogicalDirection.Forward);
                        int matchIdx = text.IndexOf(target, StringComparison.OrdinalIgnoreCase);
                        if (matchIdx != -1)
                        {
                            TextPointer start = p.GetPositionAtOffset(matchIdx);
                            TextPointer end = p.GetPositionAtOffset(matchIdx + target.Length);
                            var textRange = new TextRange(start, end);
                            textRange.Text = "";
                            break;
                        }
                    }
                }
            }
        }
        private void RenameHashtagInDocument(string tag, string newTag)
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string docText = range.Text;
            string target = "#" + tag;
            int idx = docText.IndexOf(target, StringComparison.OrdinalIgnoreCase);
            if (idx != -1)
            {
                for (TextPointer p = NoteRichTextBox.Document.ContentStart; p != null && p.CompareTo(NoteRichTextBox.Document.ContentEnd) < 0; p = p.GetNextContextPosition(LogicalDirection.Forward))
                {
                    if (p.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.Text)
                    {
                        string text = p.GetTextInRun(LogicalDirection.Forward);
                        int matchIdx = text.IndexOf(target, StringComparison.OrdinalIgnoreCase);
                        if (matchIdx != -1)
                        {
                            TextPointer start = p.GetPositionAtOffset(matchIdx);
                            TextPointer end = p.GetPositionAtOffset(matchIdx + target.Length);
                            var textRange = new TextRange(start, end);
                            textRange.Text = "#" + newTag;
                            break;
                        }
                    }
                }
            }
        }
        private void UpdateTagsDisplay()
        {
            var tags = DatabaseHelper.GetNoteTags(_noteId);
            if (tags.Count > 0)
            {
                TagsTextBlock.Text = string.Join(", ", tags);
            }
            else
            {
                TagsTextBlock.Text = "No tags";
            }
        }
        private void ParseAndSaveWikiLinks(string plainText)
        {
            try
            {
                var linkedTitles = NoteContentHelper.ExtractWikiLinks(plainText);
                var currentLinks = DatabaseHelper.GetNoteConnections()
                    .Where(c => c.FromNoteId == _noteId || c.ToNoteId == _noteId)
                    .ToList();

                foreach (var title in linkedTitles)
                {
                    var target = DatabaseHelper.GetNoteByTitle(title);
                    if (target != null && target.Id != _noteId)
                        DatabaseHelper.AddNoteConnection(_noteId, target.Id);
                }

                RefreshBacklinksPanel();
            }
            catch { }
        }
    }
}
