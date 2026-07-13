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

        private void ApplyColor()
        {
            string colorName = _note.Color;
            if (!ColorProfiles.ContainsKey(colorName))
                colorName = "yellow";

            var profile = ColorProfiles[colorName];

            WindowBorder.Background = profile.DarkBg;
            WindowBorder.BorderBrush = profile.DarkHeader;
            HeaderGrid.Background = profile.DarkHeader;
            
            TitleTextBlock.Foreground = profile.DarkText;
            NoteTitleTextBox.Foreground = profile.DarkText;
            NoteTitleTextBox.CaretBrush = profile.DarkText;
            NoteRichTextBox.Foreground = profile.DarkText;

            if (profile.DarkText is SolidColorBrush scb)
            {
                TitleDivider.Background = new SolidColorBrush(Color.FromArgb(50, scb.Color.R, scb.Color.G, scb.Color.B));
            }
            else
            {
                TitleDivider.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            }

            var wndHelper = new WindowInteropHelper(this);
            Win32Helper.EnableMica(wndHelper.Handle, true);
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }

        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            var parent = Application.Current.MainWindow as MainWindow;
            parent?.CreateNewNote();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Style = (Style)FindResource(typeof(ContextMenu));

            var colorsItem = new MenuItem { Header = "Change Color" };
            foreach (var profile in ColorProfiles)
            {
                var item = new MenuItem { Header = profile.Value.Name };
                string colorKey = profile.Key;
                item.Click += (s, args) => ChangeColor(colorKey);
                colorsItem.Items.Add(item);
            }
            menu.Items.Add(colorsItem);

            var categoryItem = new MenuItem { Header = "Move to Category" };
            string currentCat = _note.Category ?? "General";
            var allNotes = DatabaseHelper.ListNotes(null, null);
            var existingCats = allNotes
                .Select(n => n.Category ?? "General")
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            foreach (var cat in existingCats)
            {
                var subItem = new MenuItem { Header = cat, IsCheckable = true, IsChecked = cat == currentCat };
                string catCopy = cat;
                subItem.Click += (s, args) => UpdateCategory(catCopy);
                categoryItem.Items.Add(subItem);
            }
            var newCatSub = new MenuItem { Header = "+ New Category..." };
            newCatSub.Click += (s, args) =>
            {
                var dialog = new InputDialog("Enter a new category name:", "New Category", currentCat)
                {
                    Owner = this
                };
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Answer))
                    UpdateCategory(dialog.Answer.Trim());
            };
            categoryItem.Items.Add(new Separator());
            categoryItem.Items.Add(newCatSub);
            menu.Items.Add(categoryItem);
            menu.Items.Add(new Separator());

            var copyItem = new MenuItem { Header = "Copy Note Text" };
            copyItem.Click += (s, args) => CopyNoteToClipboard();
            menu.Items.Add(copyItem);

            var exportItem = new MenuItem { Header = "Export Note" };
            var exportDocxItem = new MenuItem { Header = "Export as Word (.docx)" };
            exportDocxItem.Click += (s, args) => ExportNote("docx");
            exportItem.Items.Add(exportDocxItem);
            var exportPdfItem = new MenuItem { Header = "Export as PDF (.pdf)" };
            exportPdfItem.Click += (s, args) => ExportNote("pdf");
            exportItem.Items.Add(exportPdfItem);
            menu.Items.Add(exportItem);
            menu.Items.Add(new Separator());

            var saveAsTemplateItem = new MenuItem { Header = "Save as Template" };
            saveAsTemplateItem.Click += (s, args) =>
            {
                SaveNoteContent();
                DatabaseHelper.SetNoteIsTemplate(_noteId, true);
                
                var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
                if (main != null)
                {
                    main.ShowStatusToast("Saved as template ✅");
                }
            };
            menu.Items.Add(saveAsTemplateItem);
            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "Delete Note" };
            deleteItem.Click += (s, args) => DeleteNote();
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
        }

        private void CopyNoteToClipboard()
        {
            var range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            if (!string.IsNullOrEmpty(range.Text))
            {
                Clipboard.SetText(range.Text);
            }
        }

        private void ExportNote(string format)
        {
            bool isDocx = format == "docx";
            string title = string.IsNullOrWhiteSpace(_note.Title) ? "Sticky Note" : _note.Title;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = isDocx ? "Export Note as Word Document" : "Export Note as PDF",
                FileName = SanitizeFileName(title),
                DefaultExt = isDocx ? ".docx" : ".pdf",
                Filter = isDocx ? "Word Document (*.docx)|*.docx" : "PDF Document (*.pdf)|*.pdf"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var blocks = NoteExportHelper.ExtractExportBlocks(NoteRichTextBox.Document);
                if (isDocx)
                    NoteExportHelper.WriteDocx(dialog.FileName, title, blocks);
                else
                    NoteExportHelper.WritePdf(dialog.FileName, title, blocks);

                MessageBox.Show("Note exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Sticky Note" : name;
        }

        public void ChangeColor(string colorKey)
        {
            _note.Color = colorKey;
            DatabaseHelper.UpdateNote(_note);
            ApplyColor();
            NotifyNotesChanged();
        }

        public void UpdateCategory(string newCategory)
        {
            _note.Category = newCategory;
            DatabaseHelper.UpdateNote(_note);
            NotifyNotesChanged();
            RefreshCategoryDropdown();
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

        private const string AddNewCategoryOption = "+ New Category...";
        private bool _isRefreshingCategoryCombo;

        private void RefreshCategoryDropdown()
        {
            _isRefreshingCategoryCombo = true;
            try
            {
                string currentCategory = _note.Category ?? "General";
                var categories = DatabaseHelper.ListNotes(null, null)
                    .Select(n => n.Category ?? "General")
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();
                if (!categories.Contains(currentCategory)) categories.Add(currentCategory);
                categories.Add(AddNewCategoryOption);

                CategoryComboBox.ItemsSource = categories;
                CategoryComboBox.SelectedItem = currentCategory;
            }
            finally
            {
                _isRefreshingCategoryCombo = false;
            }
        }

        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingCategoryCombo) return;
            if (CategoryComboBox.SelectedItem is not string selected) return;

            if (selected == AddNewCategoryOption)
            {
                var dialog = new InputDialog("Enter a new category name:", "New Category", _note.Category ?? "General") { Owner = this };
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Answer))
                {
                    UpdateCategory(dialog.Answer.Trim());
                }
                else
                {
                    RefreshCategoryDropdown();
                }
                return;
            }

            if (selected != (_note.Category ?? "General"))
            {
                UpdateCategory(selected);
            }
        }

        private void DeleteNote()
        {
            var res = MessageBox.Show("Are you sure you want to delete this note?", "Delete Note", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                if (!string.IsNullOrEmpty(_note.ImagePath) && File.Exists(_note.ImagePath))
                {
                    try { File.Delete(_note.ImagePath); } catch {}
                }

                DatabaseHelper.DeleteNote(_noteId);
                NotifyNotesChanged();
                this.Close();
            }
        }

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

        private static string GetFileGlyph(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "📕",
                ".doc" or ".docx" => "📝",
                ".xls" or ".xlsx" or ".csv" => "📊",
                ".ppt" or ".pptx" => "📽",
                ".zip" or ".rar" or ".7z" => "🗜",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "🖼",
                ".mp3" or ".wav" or ".flac" => "🎵",
                ".mp4" or ".mov" or ".avi" or ".mkv" => "🎬",
                _ => "📎"
            };
        }

        private void RefreshAttachmentsPanel()
        {
            AttachmentsPanel.Children.Clear();
            var attachments = DatabaseHelper.GetNoteAttachments(_noteId);

            foreach (var attachment in attachments)
            {
                AttachmentsPanel.Children.Add(CreateAttachmentChip(attachment));
            }

            var addChip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand
            };
            addChip.Child = new TextBlock { Text = "+ Add File", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)) };
            addChip.MouseLeftButtonUp += (s, e) => AddFilesViaDialog();
            AttachmentsPanel.Children.Add(addChip);
        }

        private Border CreateAttachmentChip(NoteAttachment attachment)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 6, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = attachment.FileName
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock { Text = GetFileGlyph(attachment.FileName), FontSize = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(new TextBlock
            {
                Text = attachment.FileName,
                FontSize = 10.5,
                Foreground = Brushes.White,
                MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            var removeButton = new TextBlock
            {
                Text = " ✕",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0x52, 0x52)),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            removeButton.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                var result = MessageBox.Show($"Remove attachment \"{attachment.FileName}\"?", "Remove Attachment", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    DatabaseHelper.DeleteAttachment(attachment.Id);
                    RefreshAttachmentsPanel();
                }
            };
            stack.Children.Add(removeButton);

            chip.Child = stack;
            chip.MouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource == removeButton) return;
                OpenAttachment(attachment);
            };

            return chip;
        }

        private static void OpenAttachment(NoteAttachment attachment)
        {
            if (!File.Exists(attachment.FilePath))
            {
                MessageBox.Show("This file no longer exists on disk.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(attachment.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open file: {ex.Message}", "Open Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AddFilesViaDialog()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Attach files to note",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                AttachFiles(dialog.FileNames);
            }
        }

        private async void AttachFiles(IEnumerable<string> filePaths)
        {
            foreach (var path in filePaths)
            {
                try
                {
                    await DatabaseHelper.AddAttachmentAsync(_noteId, path);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Couldn't attach \"{Path.GetFileName(path)}\": {ex.Message}", "Attach Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            RefreshAttachmentsPanel();
            NotifyNotesChanged();
        }

        private void Editor_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropHintBorder.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void Editor_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        private void Editor_DragLeave(object sender, DragEventArgs e)
        {
            DropHintBorder.Visibility = Visibility.Collapsed;
        }

        private void Editor_Drop(object sender, DragEventArgs e)
        {
            DropHintBorder.Visibility = Visibility.Collapsed;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] filePaths && filePaths.Length > 0)
            {
                AttachFiles(filePaths.Where(File.Exists));
            }
            e.Handled = true;
        }

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

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            SaveGeometry();
        }

        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SaveGeometry();
        }

        private void SaveGeometry()
        {
            if (_isLoaded)
            {
                _note.X = (int)this.Left;
                _note.Y = (int)this.Top;
                _note.W = (int)this.Width;
                _note.H = (int)this.Height;
                DatabaseHelper.UpdateNote(_note);
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

        private void AiFormatButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            var summarizeItem = new MenuItem { Header = "Summarize Note" };
            summarizeItem.Click += async (s, args) => await ApplyAiFormatAsync("Summarize this text in 2-3 short sentences, preserving key details: ");
            menu.Items.Add(summarizeItem);

            var bulletItem = new MenuItem { Header = "Format as Bullet Points" };
            bulletItem.Click += async (s, args) => await ApplyAiFormatAsync("Rewrite this text as a clean bulleted list, preserving all key details: ");
            menu.Items.Add(bulletItem);

            var grammarItem = new MenuItem { Header = "Correct Grammar & Spelling" };
            grammarItem.Click += async (s, args) => await ApplyAiFormatAsync("Fix all spelling, punctuation, and grammatical errors in this text. Do not change style or rewrite unnecessarily, just fix mistakes: ");
            menu.Items.Add(grammarItem);

            var professionalItem = new MenuItem { Header = "Rewrite Professionally" };
            professionalItem.Click += async (s, args) => await ApplyAiFormatAsync("Rewrite this text to have a highly professional, polite, and clear business tone: ");
            menu.Items.Add(professionalItem);

            menu.Items.Add(new Separator());

            var actionItemsItem = new MenuItem { Header = "Extract Action Items" };
            actionItemsItem.Click += async (s, args) => await ExtractActionItemsAsync();
            menu.Items.Add(actionItemsItem);

            menu.IsOpen = true;
        }

        private async void AiSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            await InsertTldrSummaryAsync();
        }

        private async System.Threading.Tasks.Task InsertTldrSummaryAsync()
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string plainText = range.Text.Trim();
            if (string.IsNullOrEmpty(plainText)) return;

            var oldCursor = this.Cursor;
            this.Cursor = Cursors.Wait;
            try
            {
                string prompt = "Summarize the following text in 1-2 very short, concise sentences (acting as a TL;DR summary of the main points):\n\n" + plainText;
                string aiOutput = await AiHelper.GenerateTextAsync(prompt);
                if (!string.IsNullOrEmpty(aiOutput))
                {
                    var tldrParagraph = new Paragraph();
                    tldrParagraph.Inlines.Add(new Run("TL;DR: ") { FontWeight = FontWeights.Bold });
                    tldrParagraph.Inlines.Add(new Run(aiOutput) { FontStyle = FontStyles.Italic });
                    tldrParagraph.Margin = new Thickness(0, 0, 0, 6);

                    var divider = new Paragraph(new Run("――――――――――――――――――――"))
                    {
                        Foreground = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                        Margin = new Thickness(0, 0, 0, 10)
                    };

                    if (NoteRichTextBox.Document.Blocks.FirstBlock != null)
                    {
                        NoteRichTextBox.Document.Blocks.InsertBefore(NoteRichTextBox.Document.Blocks.FirstBlock, tldrParagraph);
                        NoteRichTextBox.Document.Blocks.InsertAfter(tldrParagraph, divider);
                    }
                    else
                    {
                        NoteRichTextBox.Document.Blocks.Add(tldrParagraph);
                        NoteRichTextBox.Document.Blocks.Add(divider);
                    }

                    SaveNoteContent();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI TL;DR generation failed: " + ex.Message, "AI Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                this.Cursor = oldCursor;
            }
        }

        private async System.Threading.Tasks.Task ExtractActionItemsAsync()
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string plainText = range.Text.Trim();
            if (string.IsNullOrEmpty(plainText)) return;

            var oldCursor = this.Cursor;
            this.Cursor = Cursors.Wait;
            try
            {
                string prompt = "Extract any action items, to-dos, or follow-up tasks mentioned in the following text. " +
                    "Respond with ONLY a JSON array of short task strings (no explanations, no markdown, no code fences). " +
                    "If there are no action items, respond with an empty array [].\n\nText:\n" + plainText;

                string aiOutput = await AiHelper.GenerateTextAsync(prompt);
                var tasks = AiHelper.ParseJsonStringArray(aiOutput)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                if (tasks.Count == 0)
                {
                    MessageBox.Show("No action items were found in this note.", "Extract Action Items", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var heading = new Paragraph(new Run("Action Items")) { FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 10, 0, 3) };
                NoteRichTextBox.Document.Blocks.Add(heading);
                foreach (var task in tasks)
                {
                    NoteRichTextBox.Document.Blocks.Add(new Paragraph(new Run(UncheckedGlyph + task.Trim())));
                }

                SaveNoteContent();
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI extraction failed: " + ex.Message, "AI Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                this.Cursor = oldCursor;
            }
        }

        private async System.Threading.Tasks.Task ApplyAiFormatAsync(string promptPrefix)
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string plainText = range.Text.Trim();
            if (string.IsNullOrEmpty(plainText)) return;

            var oldCursor = this.Cursor;
            this.Cursor = Cursors.Wait;
            try
            {
                string prompt = promptPrefix + "\n\nText:\n" + plainText;
                string aiOutput = await AiHelper.GenerateTextAsync(prompt);
                if (!string.IsNullOrEmpty(aiOutput))
                {
                    NoteRichTextBox.Document.Blocks.Clear();
                    NoteRichTextBox.Document.Blocks.Add(new Paragraph(new Run(aiOutput)));
                    SaveNoteContent();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI formatting failed: " + ex.Message, "AI Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                this.Cursor = oldCursor;
            }
        }

        private Border? _typingBubble;
        private bool _isMarkdownPreviewActive = false;
        private bool _isAiChatActive = false;

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

        private void AiChatToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isAiChatActive = !_isAiChatActive;

            if (_isAiChatActive)
            {
                AiChatPanel.Visibility = Visibility.Visible;
                AiPromptTextBox.Focus();

                if (ChatHistoryPanel.Children.Count == 0)
                {
                    AddChatBubble("Hello! I am your AI assistant. You can ask me questions about this note or its attached screenshot.", false);
                }
            }
            else
            {
                AiChatPanel.Visibility = Visibility.Collapsed;
            }
        }

        private async void SendAiPrompt_Click(object sender, RoutedEventArgs e)
        {
            await ProcessAiChatQueryAsync();
        }

        private async void AiPromptTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ProcessAiChatQueryAsync();
            }
        }

        private async System.Threading.Tasks.Task ProcessAiChatQueryAsync()
        {
            string query = AiPromptTextBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            AiPromptTextBox.Text = "";
            AddChatBubble(query, true);
            ShowTypingIndicator();

            try
            {
                TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                string noteText = range.Text.Trim();

                string prompt = $"You are a helpful desktop note assistant. Answer the user's question contextually based on the note text provided below. If there are screenshots attached, their OCR text is also included. Answer clearly and keep it short.\n\nNote Context:\n{noteText}\n\nUser Question:\n{query}";

                string aiResponse = await AiHelper.GenerateTextAsync(prompt);
                
                RemoveTypingIndicator();
                if (string.IsNullOrEmpty(aiResponse))
                {
                    AddChatBubble("Sorry, I could not generate a response. Please check if Ollama is running.", false);
                }
                else
                {
                    AddChatBubble(aiResponse, false);
                }
            }
            catch (System.Exception ex)
            {
                RemoveTypingIndicator();
                AddChatBubble("Error during processing: " + ex.Message, false);
            }
        }

        private void AddChatBubble(string text, bool isUser)
        {
            var border = new Border
            {
                Background = isUser ? new SolidColorBrush(Color.FromRgb(0, 132, 255)) : new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = this.ActualWidth * 0.75
            };

            var tb = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            border.Child = tb;
            ChatHistoryPanel.Children.Add(border);
            ChatScrollViewer.ScrollToEnd();
        }

        private void ShowTypingIndicator()
        {
            _typingBubble = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var tb = new TextBlock
            {
                Text = "AI is thinking...",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                FontStyle = FontStyles.Italic
            };

            _typingBubble.Child = tb;
            ChatHistoryPanel.Children.Add(_typingBubble);
            ChatScrollViewer.ScrollToEnd();
        }

        private void RemoveTypingIndicator()
        {
            if (_typingBubble != null)
            {
                ChatHistoryPanel.Children.Remove(_typingBubble);
                _typingBubble = null;
            }
        }

        private void NoteRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (NoteRichTextBox == null || FormatToolbarPopup == null) return;

            if (NoteRichTextBox.Selection.IsEmpty)
            {
                FormatToolbarPopup.IsOpen = false;
            }
            else
            {
                var start = NoteRichTextBox.Selection.Start;
                var end = NoteRichTextBox.Selection.End;

                Rect rectStart = start.GetCharacterRect(LogicalDirection.Forward);
                Rect rectEnd = end.GetCharacterRect(LogicalDirection.Backward);

                double selectionLeft = Math.Min(rectStart.Left, rectEnd.Left);
                double selectionRight = Math.Max(rectStart.Right, rectEnd.Right);
                double selectionTop = rectStart.Top;

                double midpointX = selectionLeft + (selectionRight - selectionLeft) / 2;

                FormatToolbarPopup.PlacementTarget = NoteRichTextBox;
                FormatToolbarPopup.HorizontalOffset = midpointX - 60;
                FormatToolbarPopup.VerticalOffset = selectionTop - 35;
                FormatToolbarPopup.IsOpen = true;
            }
        }

        private void FormatBold_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleBold.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }

        private void FormatItalic_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleItalic.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }

        private void FormatUnderline_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleUnderline.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }

        private void FormatStrikethrough_Click(object sender, RoutedEventArgs e)
        {
            var range = NoteRichTextBox.Selection;
            var currentDecoration = range.GetPropertyValue(Inline.TextDecorationsProperty);
            if (currentDecoration == TextDecorations.Strikethrough)
            {
                range.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            }
            else
            {
                range.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
            }
            NoteRichTextBox.Focus();
        }

        private void FormatHighlight_Click(object sender, RoutedEventArgs e)
        {
            var range = NoteRichTextBox.Selection;
            var currentBackground = range.GetPropertyValue(TextElement.BackgroundProperty);

            var yellowBrush = new SolidColorBrush(Color.FromArgb(80, 255, 235, 59));
            if (currentBackground is SolidColorBrush brush && brush.Color == yellowBrush.Color)
            {
                range.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            }
            else
            {
                range.ApplyPropertyValue(TextElement.BackgroundProperty, yellowBrush);
            }
            NoteRichTextBox.Focus();
        }

        private static readonly Regex UrlRegex = new Regex(
            @"^(https?://[^\s]+|www\.[^\s]+\.[^\s]+)$", RegexOptions.IgnoreCase);

        private bool _isAutoFormatting;

        private void FormatHeading1_Click(object sender, RoutedEventArgs e) => ToggleHeading(18.0);

        private void FormatHeading2_Click(object sender, RoutedEventArgs e) => ToggleHeading(15.0);

        private void ToggleHeading(double targetSize)
        {
            var paragraph = NoteRichTextBox.Selection.Start.Paragraph ?? NoteRichTextBox.CaretPosition.Paragraph;
            if (paragraph == null) return;

            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            bool isThisHeading = range.GetPropertyValue(TextElement.FontSizeProperty) is double size && Math.Abs(size - targetSize) < 0.5;

            if (isThisHeading)
            {
                range.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
                range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            }
            else
            {
                range.ApplyPropertyValue(TextElement.FontSizeProperty, targetSize);
                range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            }
            NoteRichTextBox.Focus();
        }

        private void FormatTextColor_Click(object sender, RoutedEventArgs e)
        {
            var colors = new (string Name, Color Color)[]
            {
                ("White",  Colors.White),
                ("Red",    Color.FromRgb(0xff, 0x6b, 0x6b)),
                ("Orange", Color.FromRgb(0xff, 0xa5, 0x4d)),
                ("Yellow", Color.FromRgb(0xff, 0xd7, 0x00)),
                ("Green",  Color.FromRgb(0x6b, 0xff, 0x8f)),
                ("Blue",   Color.FromRgb(0x4d, 0xb8, 0xff)),
                ("Purple", Color.FromRgb(0xc9, 0x8b, 0xff)),
            };

            var menu = new ContextMenu();
            foreach (var (name, color) in colors)
            {
                var swatch = new Border
                {
                    Width = 13,
                    Height = 13,
                    Background = new SolidColorBrush(color),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var stack = new StackPanel { Orientation = Orientation.Horizontal };
                stack.Children.Add(swatch);
                stack.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });

                var item = new MenuItem { Header = stack };
                var capturedColor = color;
                item.Click += (s, args) =>
                {
                    if (!NoteRichTextBox.Selection.IsEmpty)
                        NoteRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(capturedColor));
                    NoteRichTextBox.Focus();
                };
                menu.Items.Add(item);
            }

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private void FormatIncreaseFontSize_Click(object sender, RoutedEventArgs e) => AdjustFontSize(2);

        private void FormatDecreaseFontSize_Click(object sender, RoutedEventArgs e) => AdjustFontSize(-2);

        private void AdjustFontSize(double delta)
        {
            if (NoteRichTextBox.Selection.IsEmpty) return;

            double current = NoteRichTextBox.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size ? size : 14.0;
            double next = Math.Max(8.0, Math.Min(48.0, current + delta));
            NoteRichTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, next);
            NoteRichTextBox.Focus();
        }

        private void FormatBlockquote_Click(object sender, RoutedEventArgs e)
        {
            var paragraph = NoteRichTextBox.Selection.Start.Paragraph ?? NoteRichTextBox.CaretPosition.Paragraph;
            if (paragraph == null) return;

            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            bool isQuote = paragraph.BorderThickness.Left > 0;

            if (isQuote)
            {
                paragraph.BorderThickness = new Thickness(0);
                paragraph.Padding = new Thickness(0);
                paragraph.FontStyle = FontStyles.Normal;
                range.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White);
            }
            else
            {
                paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
                paragraph.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                paragraph.Padding = new Thickness(10, 2, 0, 2);
                paragraph.Margin = new Thickness(0, 4, 0, 4);
                paragraph.FontStyle = FontStyles.Italic;
                range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)));
            }
            NoteRichTextBox.Focus();
        }

        private void FormatNumberedList_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleNumbering.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }

        private void FormatIndent_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.IncreaseIndentation.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }

        private void FormatOutdent_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.DecreaseIndentation.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }

        private void InsertDivider_Click(object sender, RoutedEventArgs e)
        {
            var divider = new Paragraph(new Run(""))
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 10, 0, 10)
            };

            var caretParagraph = NoteRichTextBox.CaretPosition.Paragraph;
            if (caretParagraph != null)
                NoteRichTextBox.Document.Blocks.InsertAfter(caretParagraph, divider);
            else
                NoteRichTextBox.Document.Blocks.Add(divider);

            var following = new Paragraph(new Run(""));
            NoteRichTextBox.Document.Blocks.InsertAfter(divider, following);
            NoteRichTextBox.CaretPosition = following.ContentStart;

            NoteRichTextBox.Focus();
        }

        private void FormatClear_Click(object sender, RoutedEventArgs e)
        {
            if (NoteRichTextBox.Selection.IsEmpty) return;

            var selection = NoteRichTextBox.Selection;
            selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
            selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
            selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"));
            selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White);
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            NoteRichTextBox.Focus();
        }

        private void FormatCodeBlock_Click(object sender, RoutedEventArgs e)
        {
            var codeFont = new FontFamily("Consolas");
            var codeBackground = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
            var codeForeground = new SolidColorBrush(Color.FromRgb(0xa8, 0xff, 0x78));

            if (!NoteRichTextBox.Selection.IsEmpty)
            {
                var selection = NoteRichTextBox.Selection;
                selection.ApplyPropertyValue(TextElement.FontFamilyProperty, codeFont);
                selection.ApplyPropertyValue(TextElement.BackgroundProperty, codeBackground);
                selection.ApplyPropertyValue(TextElement.ForegroundProperty, codeForeground);
            }
            else
            {
                var paragraph = new Paragraph(new Run(""))
                {
                    FontFamily = codeFont,
                    Background = codeBackground,
                    Foreground = codeForeground,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Margin = new Thickness(0, 4, 0, 4)
                };

                var caretParagraph = NoteRichTextBox.CaretPosition.Paragraph;
                if (caretParagraph != null)
                    NoteRichTextBox.Document.Blocks.InsertAfter(caretParagraph, paragraph);
                else
                    NoteRichTextBox.Document.Blocks.Add(paragraph);

                NoteRichTextBox.CaretPosition = paragraph.ContentStart;
            }
            NoteRichTextBox.Focus();
        }

        private void FormatBulletList_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleBullets.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }

        private const string UncheckedGlyph = "☐ ";
        private const string CheckedGlyph = "☑ ";

        private void InsertCheckbox_Click(object sender, RoutedEventArgs e)
        {
            var caretParagraph = NoteRichTextBox.CaretPosition.Paragraph;
            var newParagraph = new Paragraph();
            var run = new Run(UncheckedGlyph + "New task");
            newParagraph.Inlines.Add(run);

            if (caretParagraph != null)
                NoteRichTextBox.Document.Blocks.InsertAfter(caretParagraph, newParagraph);
            else
                NoteRichTextBox.Document.Blocks.Add(newParagraph);

            var textStart = run.ContentStart.GetPositionAtOffset(UncheckedGlyph.Length) ?? run.ContentStart;
            NoteRichTextBox.Selection.Select(textStart, run.ContentEnd);
            NoteRichTextBox.Focus();
        }

        private static Run? GetChecklistRun(Paragraph? paragraph)
        {
            if (paragraph?.Inlines.FirstInline is Run run &&
                (run.Text.StartsWith(UncheckedGlyph, StringComparison.Ordinal) || run.Text.StartsWith(CheckedGlyph, StringComparison.Ordinal)))
                return run;
            return null;
        }

        private static Run? GetChecklistRunAt(TextPointer? position)
        {
            if (position == null) return null;
            var run = position.Parent as Run ?? position.GetAdjacentElement(LogicalDirection.Forward) as Run;
            if (run == null) return null;

            var checklistRun = GetChecklistRun(run.Parent as Paragraph);
            if (checklistRun != run) return null;

            var glyphEnd = run.ContentStart.GetPositionAtOffset(UncheckedGlyph.Length);
            if (glyphEnd == null || position.CompareTo(glyphEnd) > 0) return null;

            return checklistRun;
        }

        private static void ToggleChecklistItem(Run checklistRun)
        {
            bool wasChecked = checklistRun.Text.StartsWith(CheckedGlyph, StringComparison.Ordinal);
            string rest = checklistRun.Text.Substring(UncheckedGlyph.Length);
            checklistRun.Text = (wasChecked ? UncheckedGlyph : CheckedGlyph) + rest;

            var decoration = wasChecked ? null : TextDecorations.Strikethrough;
            var foreground = wasChecked ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            checklistRun.TextDecorations = decoration;
            checklistRun.Foreground = foreground;

            if (checklistRun.Parent is Paragraph paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline == checklistRun) continue;
                    inline.TextDecorations = decoration;
                    inline.Foreground = foreground;
                }
            }
        }

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
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            }
            catch { }
        }

        private void NoteRichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var position = NoteRichTextBox.GetPositionFromPoint(e.GetPosition(NoteRichTextBox), true);

            if (Keyboard.Modifiers == ModifierKeys.Control)
            {
                var hyperlink = FindAncestorHyperlink(position?.Parent);
                if (hyperlink?.NavigateUri != null)
                {
                    OpenUrl(hyperlink.NavigateUri);
                    e.Handled = true;
                    return;
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

        private void RefreshBacklinksPanel()
        {
            var backlinks = DatabaseHelper.GetBacklinks(_noteId);
            Dispatcher.Invoke(() =>
            {
                BacklinksToggleButton.Visibility = backlinks.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

                BacklinksNotePanel.Children.Clear();
                foreach (var note in backlinks)
                {
                    var chip = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(50, 0, 132, 255)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(100, 0, 132, 255)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(4),
                        Padding = new Thickness(8, 3, 8, 3),
                        Margin = new Thickness(0, 0, 6, 4),
                        Cursor = Cursors.Hand,
                        ToolTip = note.Title,
                        Tag = note.Id,
                    };
                    chip.Child = new TextBlock
                    {
                        Text = note.Title.Length > 20 ? note.Title.Substring(0, 18) + "…" : note.Title,
                        Foreground = Brushes.White,
                        FontSize = 10,
                    };
                    chip.MouseLeftButtonDown += (s, e) =>
                    {
                        if (s is Border b && b.Tag is int nid)
                        {
                            if (Owner is MainWindow main)
                                main.OpenNoteWindow(nid);
                        }
                    };
                    BacklinksNotePanel.Children.Add(chip);
                }
            });
        }

        private void BacklinksToggleButton_Click(object sender, RoutedEventArgs e)
        {
            bool isVisible = BacklinksPanel.Visibility == Visibility.Visible;
            AiChatPanel.Visibility = Visibility.Collapsed;
            TimeMachinePanel.Visibility = Visibility.Collapsed;
            BacklinksPanel.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void CloseBacklinks_Click(object sender, RoutedEventArgs e)
        {
            BacklinksPanel.Visibility = Visibility.Collapsed;
        }

        private void OnPreviewExecuted(object sender, ExecutedRoutedEventArgs e)
        {
            if (e.Command == ApplicationCommands.Paste)
            {
                if (HandleClipboardPaste())
                {
                    e.Handled = true;
                }
            }
        }

        private bool HandleClipboardPaste()
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                {
                    var files = Clipboard.GetFileDropList();
                    if (files != null && files.Count > 0)
                    {
                        var list = new List<string>();
                        foreach (var f in files)
                        {
                            if (f != null) list.Add(f);
                        }
                        AttachFiles(list);
                        return true;
                    }
                }

                if (Clipboard.ContainsImage())
                {
                    var image = Clipboard.GetImage();
                    if (image != null)
                    {
                        string tempFileName = $"PastedImage_{Guid.NewGuid():N}.png";
                        string tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

                        using (var fileStream = new FileStream(tempFilePath, FileMode.Create))
                        {
                            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));
                            encoder.Save(fileStream);
                        }

                        AttachFiles(new[] { tempFilePath });

                        try { File.Delete(tempFilePath); } catch {}
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to process paste: " + ex.Message, "Paste Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return false;
        }
    }
}
