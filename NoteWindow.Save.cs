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
        private void NoteTitleTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoaded)
            {
                _saveTimer.Stop();
                _saveTimer.Start();
            }
        }
        public void FocusTitle()
        {
            NoteTitleTextBox.Focus();
            NoteTitleTextBox.SelectAll();
        }
        private void NoteRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoaded)
            {
                _saveTimer.Stop();
                _saveTimer.Start();
            }

            if (!_isAutoFormatting)
            {
                AutoDetectUrl(e);
            }
        }
        private void UpdateWordCount()
        {
            if (WordCountText == null) return;

            string plainText = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd).Text;
            int wordCount = plainText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;

            if (wordCount == 0)
            {
                WordCountText.Text = "";
                return;
            }

            WordCountText.Text = $"{wordCount} word{(wordCount == 1 ? "" : "s")}";
        }
        private void SaveTimer_Tick(object? sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNoteContent();
        }
        private void SaveNoteContent()
        {
            if (_note == null) return;

            string title = NoteTitleTextBox.Text.Trim();
            TitleTextBlock.Text = string.IsNullOrEmpty(title) ? "Sticky Note" : title;

            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string xamlText = NoteContentHelper.SaveRange(range);
            string plainText = range.Text.Trim();

            _note.Title = title;
            _note.Content = xamlText;

            DatabaseHelper.UpdateNote(_note);
            ParseAndSaveWikiLinks(plainText);

            var newTags = new HashSet<string>();
            string fullText = title + " " + plainText;
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(fullText, @"\B#([a-zA-Z0-9_-]+)"))
            {
                string tag = match.Groups[1].Value.ToLower().Trim();
                if (!string.IsNullOrEmpty(tag))
                {
                    newTags.Add(tag);
                }
            }

            var oldTags = DatabaseHelper.GetNoteTags(_noteId);
            bool tagsChanged = false;

            foreach (var tag in newTags)
            {
                if (!oldTags.Contains(tag))
                {
                    DatabaseHelper.AddTagToNote(_noteId, tag);
                    tagsChanged = true;
                }
            }

            foreach (var tag in oldTags)
            {
                if (!newTags.Contains(tag))
                {
                    DatabaseHelper.RemoveTagFromNote(_noteId, tag);
                    tagsChanged = true;
                }
            }

            if (tagsChanged)
            {
                UpdateTagsDisplay();
            }

            NotifyNotesChanged();

            if (_lastHistoryContent != xamlText)
            {
                int diff = Math.Abs(plainText.Length - _lastHistoryPlain.Length);
                bool timeElapsed = (DateTime.Now - _lastHistoryTime).TotalSeconds >= 15;

                if (diff >= 15 || timeElapsed)
                {
                    if (!string.IsNullOrEmpty(_lastHistoryContent))
                    {
                        DatabaseHelper.AddNoteHistoryEntry(_note.Id, _lastHistoryContent);
                    }
                    _lastHistoryContent = xamlText;
                    _lastHistoryPlain = plainText;
                    _lastHistoryTime = DateTime.Now;
                }
            }

            UpdateWordCount();
        }
    }
}
