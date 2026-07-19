using System;
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


namespace StickyNotes__
{
    public partial class NoteWindow : Window
    {
        private readonly int _noteId;
        private Note _note;
        private bool _isLoaded;
        private readonly DispatcherTimer _saveTimer;
        private string _lastHistoryContent = "";
        private string _lastHistoryPlain = "";
        private DateTime _lastHistoryTime = DateTime.MinValue;
        private static readonly System.Collections.Generic.Dictionary<string, NoteColorProfile> ColorProfiles = 
            new System.Collections.Generic.Dictionary<string, NoteColorProfile>
        {
            { "yellow", new NoteColorProfile("Yellow", "#fff3c4", "#ffe9a1", "#CC221C12", "#D49A13", "#000000", "#ffffff") },
            { "green", new NoteColorProfile("Green", "#d4f7db", "#b3eebf", "#CC122018", "#1A8F54", "#000000", "#ffffff") },
            { "pink", new NoteColorProfile("Pink", "#ffd4e5", "#ffb3d2", "#CC221218", "#C2185B", "#000000", "#ffffff") },
            { "purple", new NoteColorProfile("Purple", "#ebd4ff", "#ddb3ff", "#CC1B1220", "#7B1FA2", "#000000", "#ffffff") },
            { "blue", new NoteColorProfile("Blue", "#d4ebff", "#b3ddff", "#CC121C22", "#0288D1", "#000000", "#ffffff") },
            { "charcoal", new NoteColorProfile("Charcoal", "#ececec", "#d4d4d4", "#CC1B1B1B", "#424242", "#000000", "#ffffff") }
        };
        public class NoteColorProfile
        {
            public string Name { get; }
            public System.Windows.Media.Brush LightBg { get; }
            public System.Windows.Media.Brush LightHeader { get; }
            public System.Windows.Media.Brush DarkBg { get; }
            public System.Windows.Media.Brush DarkHeader { get; }
            public System.Windows.Media.Brush LightText { get; }
            public System.Windows.Media.Brush DarkText { get; }

            public NoteColorProfile(string name, string lBg, string lHdr, string dBg, string dHdr, string lTxt, string dTxt)
            {
                Name = name;
                var converter = new System.Windows.Media.BrushConverter();
                LightBg = (System.Windows.Media.Brush)converter.ConvertFromString(lBg)!;
                LightHeader = (System.Windows.Media.Brush)converter.ConvertFromString(lHdr)!;
                DarkBg = (System.Windows.Media.Brush)converter.ConvertFromString(dBg)!;
                DarkHeader = (System.Windows.Media.Brush)converter.ConvertFromString(dHdr)!;
                LightText = (System.Windows.Media.Brush)converter.ConvertFromString(lTxt)!;
                DarkText = (System.Windows.Media.Brush)converter.ConvertFromString(dTxt)!;
            }
        }
        public NoteWindow(int noteId)
        {
            InitializeComponent();
            _noteId = noteId;
            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _saveTimer.Tick += SaveTimer_Tick;

            _note = DatabaseHelper.GetNote(_noteId) ?? new Note();
            LoadNoteData();

            CheckOllamaStatus();

            CommandManager.AddPreviewExecutedHandler(NoteRichTextBox, OnPreviewExecuted);
        }
        private async void CheckOllamaStatus()
        {
            try
            {
                if (await AiHelper.IsOllamaRunningAsync())
                {
                    AiFormatButton.Visibility = Visibility.Visible;
                    AiSummaryButton.Visibility = Visibility.Visible;
                    AiTagButton.Visibility = Visibility.Visible;
                    AiChatToggleButton.Visibility = Visibility.Visible;
                }
            }
            catch {}
        }
        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            ApplyColor();
            _isLoaded = true;
        }
        private void LoadNoteData()
        {
            if (_note == null) return;

            NoteTitleTextBox.Text = _note.Title ?? "";
            TitleTextBlock.Text = string.IsNullOrEmpty(_note.Title) ? "Sticky Note" : _note.Title;

            this.Width = _note.W ?? 300;
            this.Height = _note.H ?? 320;
            if (_note.X != null && _note.Y != null)
            {
                this.Left = _note.X.Value;
                this.Top = _note.Y.Value;
            }

            if (!string.IsNullOrEmpty(_note.ImagePath) && File.Exists(_note.ImagePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(_note.ImagePath);
                    bitmap.EndInit();
                    
                    NoteImage.Source = bitmap;
                    ImageBorder.Visibility = Visibility.Visible;
                }
                catch
                {
                    ImageBorder.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                ImageBorder.Visibility = Visibility.Collapsed;
            }

            if (!string.IsNullOrEmpty(_note.Content))
            {
                TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                if (!NoteContentHelper.TryLoadRange(range, _note.Content))
                {
                    NoteRichTextBox.Document.Blocks.Clear();
                    NoteRichTextBox.Document.Blocks.Add(new Paragraph(new Run(_note.Content)));
                }

                if (!string.IsNullOrEmpty(_note.Title) && NoteRichTextBox.Document.Blocks.Count > 0)
                {
                    var firstBlock = NoteRichTextBox.Document.Blocks.FirstBlock as Paragraph;
                    if (firstBlock != null)
                    {
                        string firstBlockText = new TextRange(firstBlock.ContentStart, firstBlock.ContentEnd).Text.Trim();
                        bool matches = firstBlockText == _note.Title || 
                                       (_note.Title.EndsWith("...") && _note.Title.Length >= 4 && firstBlockText.StartsWith(_note.Title.Substring(0, _note.Title.Length - 3)));
                        if (matches)
                        {
                            NoteRichTextBox.Document.Blocks.Remove(firstBlock);
                        }
                    }
                }
            }

            RewireInteractiveElements();

            TextRange initRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);

            var dbTags = DatabaseHelper.GetNoteTags(_noteId);
            if (dbTags.Count > 0)
            {
                string bodyText = initRange.Text;
                StringBuilder sb = new StringBuilder();
                bool addedAny = false;
                foreach (var tag in dbTags)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(bodyText, $@"\B#{System.Text.RegularExpressions.Regex.Escape(tag)}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        sb.Append(" #" + tag);
                        addedAny = true;
                    }
                }
                if (addedAny)
                {
                    TextRange endRange = new TextRange(NoteRichTextBox.Document.ContentEnd, NoteRichTextBox.Document.ContentEnd);
                    endRange.Text = sb.ToString();
                    SaveNoteContent();

                    initRange = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                }
            }

            _lastHistoryContent = _note.Content ?? "";
            _lastHistoryPlain = initRange.Text.Trim();
            _lastHistoryTime = DateTime.Now;

            UpdateTagsDisplay();
            RefreshAttachmentsPanel();
            RefreshCategoryDropdown();
            UpdateWordCount();
            RefreshBacklinksPanel();
        }
        public void ReloadNoteFromDb()
        {
            var latest = DatabaseHelper.GetNote(_noteId);
            if (latest != null)
            {
                _note = latest;
                LoadNoteData();
            }
        }
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            string title = NoteTitleTextBox.Text.Trim();
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string plainText = range.Text.Trim();
            var attachments = DatabaseHelper.GetNoteAttachments(_noteId);

            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(plainText) && (attachments == null || attachments.Count == 0))
            {
                DatabaseHelper.DeleteNote(_noteId);
                NotifyNotesChanged();
            }
            else
            {
                SaveNoteContent();
            }

            var main = Application.Current.MainWindow as MainWindow;
            main?.NotifyNoteWindowClosed(_noteId);
        }
        private void NotifyNotesChanged()
        {
            var main = Application.Current.MainWindow as MainWindow;
            main?.RefreshNotesList();
        }
    }
}
