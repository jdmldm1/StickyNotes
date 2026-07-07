using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace StickyNotes__
{
    // A OneNote-style browse/edit surface: colored category tabs on the left, a notes list in the
    // middle, and a full rich-text editor on the right -- for reviewing and editing many notes in
    // one bigger window instead of popping open each note's own small floating window.
    public partial class NoteManagerWindow : Window
    {
        private readonly MainWindow _owner;
        private string? _selectedCategory; // null = All Notes, "__favorites__" = Favorites tab
        private Note? _currentNote;
        private bool _isLoadingNote;
        private readonly DispatcherTimer _saveTimer;
        private string _lastHistoryContent = "";
        private string _lastHistoryPlain = "";
        private DateTime _lastHistoryTime = DateTime.Now;
        private bool _isAiChatActive;
        private Border? _typingBubble;

        private static readonly string[] TabAccentColors = { "#D49A13", "#1A8F54", "#C2185B", "#7B1FA2", "#0288D1", "#424242" };

        public NoteManagerWindow(MainWindow owner)
        {
            InitializeComponent();
            _owner = owner;

            _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _saveTimer.Tick += (s, e) => { _saveTimer.Stop(); SaveCurrentNote(); };
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshCategoryTabs();
            RefreshNotesList();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (_currentNote != null) SaveCurrentNote();
            _owner.RefreshNotesList();
            _owner.RefreshTagsFilter();
        }

        #region Window Chrome

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) this.DragMove();
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void MaximizeButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
            {
                this.WindowState = WindowState.Normal;
                MaximizeIcon.Content = "🗖";
            }
            else
            {
                this.WindowState = WindowState.Maximized;
                MaximizeIcon.Content = "🗗";
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => this.Close();

        #endregion

        #region Category Tabs

        private static string ColorForCategory(string category)
        {
            if (string.IsNullOrEmpty(category) || category == "General") return "#5a5a5a";

            string? customHex = DatabaseHelper.GetCategoryColor(category);
            if (!string.IsNullOrEmpty(customHex)) return customHex;

            int hash = 0;
            foreach (char c in category) hash = hash * 31 + c;
            int idx = Math.Abs(hash) % TabAccentColors.Length;
            return TabAccentColors[idx];
        }

        public void RefreshCategoryTabs()
        {
            CategoryTabsPanel.Children.Clear();

            var allNotes = DatabaseHelper.ListNotes(null, null);
            var categories = allNotes.Select(n => n.Category ?? "General").Distinct().OrderBy(c => c == "General" ? "zzz" : c).ToList();

            CategoryTabsPanel.Children.Add(BuildTab("All Notes", "#0084ff", null));
            CategoryTabsPanel.Children.Add(BuildTab("★ Favorites", "#ffc107", "__favorites__"));

            foreach (var category in categories)
            {
                CategoryTabsPanel.Children.Add(BuildTab(category, ColorForCategory(category), category));
            }
        }

        private void AddCategoryButton_Click(object sender, RoutedEventArgs e) => AddNewCategory();

        private void AddNewCategory()
        {
            var dialog = new InputDialog("Enter a name for the new category:", "New Category") { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer)) return;

            string categoryName = dialog.Answer.Trim();

            var allNotes = DatabaseHelper.ListNotes(null, null);
            bool categoryExists = allNotes.Any(n => string.Equals(n.Category ?? "General", categoryName, StringComparison.OrdinalIgnoreCase));

            if (!categoryExists)
            {
                // A category only exists in this app as a value on notes -- create a blank note
                // so the new category actually has somewhere to show up and be selected.
                int noteId = DatabaseHelper.CreateNote("", "", null, null, "yellow");
                var note = DatabaseHelper.GetNote(noteId);
                if (note != null)
                {
                    note.Category = categoryName;
                    DatabaseHelper.UpdateNote(note);
                }
                _owner.RefreshNotesList();
            }

            RefreshCategoryTabs();
            SelectCategory(categoryName);
        }

        private Border BuildTab(string label, string accentHex, string? categoryKey)
        {
            bool isSelected = _selectedCategory == categoryKey;
            var accentBrush = (Brush)new BrushConverter().ConvertFromString(accentHex)!;

            var border = new Border
            {
                Height = 38,
                Margin = new Thickness(8, 3, 8, 3),
                CornerRadius = new CornerRadius(6),
                Background = isSelected ? new SolidColorBrush(Color.FromArgb(0x33, 0xff, 0xff, 0xff)) : Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = categoryKey
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(5) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var accentBar = new Border { Background = accentBrush, CornerRadius = new CornerRadius(3, 0, 0, 3) };
            Grid.SetColumn(accentBar, 0);

            var text = new TextBlock
            {
                Text = label,
                Foreground = Brushes.White,
                FontSize = 11.5,
                FontWeight = isSelected ? FontWeights.Bold : FontWeights.Normal,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 4, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(text, 1);

            grid.Children.Add(accentBar);
            grid.Children.Add(text);
            border.Child = grid;

            if (categoryKey != null && categoryKey != "__favorites__")
            {
                border.ContextMenu = _owner.CreateCategoryContextMenu(categoryKey, () =>
                {
                    RefreshCategoryTabs();
                    _owner.RefreshNotesList();
                });
            }

            border.MouseLeftButtonUp += (s, e) => SelectCategory(categoryKey);

            return border;
        }

        private void SelectCategory(string? categoryKey)
        {
            _selectedCategory = categoryKey;
            RefreshCategoryTabs();
            RefreshNotesList();
        }

        #endregion

        #region Notes List

        private void ManagerSearchBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshNotesList();

        private void RefreshNotesList()
        {
            NotesListPanel.Children.Clear();

            string searchQuery = ManagerSearchBox.Text.Trim();
            string? categoryFilter = (_selectedCategory != null && _selectedCategory != "__favorites__") ? _selectedCategory : null;

            var notes = DatabaseHelper.ListNotes(string.IsNullOrEmpty(searchQuery) ? null : searchQuery, null, categoryFilter);

            if (_selectedCategory == "__favorites__")
            {
                notes = notes.Where(n => n.IsFavorite).ToList();
            }

            CategoryHeaderText.Text = _selectedCategory switch
            {
                null => "All Notes",
                "__favorites__" => "★ Favorites",
                _ => _selectedCategory
            };

            foreach (var note in notes)
            {
                NotesListPanel.Children.Add(BuildNoteListItem(note));
            }
        }

        private Border BuildNoteListItem(Note note)
        {
            bool isSelected = _currentNote != null && _currentNote.Id == note.Id;
            string title = string.IsNullOrEmpty(note.Title) ? "Sticky Note" : note.Title;
            string fullText = NoteContentHelper.ExtractPlainText(note.Content);
            string snippet = BuildSnippetWithoutTitle(fullText);
            if (snippet.Length > 90) snippet = snippet.Substring(0, 90) + "...";

            var border = new Border
            {
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 0, 6),
                CornerRadius = new CornerRadius(6),
                Background = isSelected ? new SolidColorBrush(Color.FromArgb(0x40, 0x00, 0x84, 0xff)) : new SolidColorBrush(Color.FromArgb(0x0C, 0xff, 0xff, 0xff)),
                Cursor = Cursors.Hand,
                Tag = note.Id
            };

            var stack = new StackPanel();
            var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
            if (note.IsFavorite)
            {
                titleRow.Children.Add(new TextBlock { Text = "★ ", Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xc1, 0x07)), FontSize = 11.5 });
            }
            titleRow.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            stack.Children.Add(titleRow);

            if (!string.IsNullOrEmpty(snippet))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = snippet,
                    Foreground = new SolidColorBrush(Color.FromArgb(0xB0, 0xff, 0xff, 0xff)),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            border.Child = stack;
            border.MouseLeftButtonUp += (s, e) => LoadNoteIntoEditor(note.Id);

            return border;
        }

        private string BuildSnippetWithoutTitle(string fullPlainText)
        {
            if (string.IsNullOrEmpty(fullPlainText)) return "";
            int firstNewline = fullPlainText.IndexOf('\n');
            if (firstNewline < 0) return "";
            return fullPlainText.Substring(firstNewline + 1).TrimStart('\r', '\n', ' ', '\t');
        }

        #endregion

        #region Editor

        private void LoadNoteIntoEditor(int noteId)
        {
            if (_currentNote != null && _currentNote.Id != noteId)
            {
                SaveCurrentNote();
            }

            var note = DatabaseHelper.GetNote(noteId);
            if (note == null) return;

            _isLoadingNote = true;
            _currentNote = note;

            EmptyStateText.Visibility = Visibility.Collapsed;
            EditorHeaderGrid.Visibility = Visibility.Visible;
            EditorToolbar.Visibility = Visibility.Visible;
            ManagerRichTextBox.Visibility = Visibility.Visible;
            EditorFooterGrid.Visibility = Visibility.Visible;

            // Chat is per-note context, so switching notes closes any chat left open for the last one.
            _isAiChatActive = false;
            AiChatPanel.Visibility = Visibility.Collapsed;
            ChatHistoryPanel.Children.Clear();

            EditorTitleBox.Text = note.Title ?? "";
            EditorFavoriteButton.Content = note.IsFavorite ? "★" : "☆";
            EditorFavoriteButton.Foreground = note.IsFavorite ? new SolidColorBrush(Color.FromRgb(0xff, 0xc1, 0x07)) : new SolidColorBrush(Color.FromArgb(0x80, 0xff, 0xff, 0xff));

            ManagerRichTextBox.Document.Blocks.Clear();
            if (!string.IsNullOrEmpty(note.Content))
            {
                TextRange range = new TextRange(ManagerRichTextBox.Document.ContentStart, ManagerRichTextBox.Document.ContentEnd);
                if (!NoteContentHelper.TryLoadRange(range, note.Content))
                {
                    ManagerRichTextBox.Document.Blocks.Add(new Paragraph(new Run(note.Content)));
                }
            }

            // The note's title is stored as the first line of its content (see SaveCurrentNote),
            // so drop it from the body -- it's already shown, editable, in the title box above
            // instead of being repeated inside the editor. Rich notes keep each line as its own
            // Paragraph, so the whole first block can just be removed -- but legacy/plain-text
            // notes can have the entire multi-line body squashed into one Paragraph/Run, in which
            // case only the text up to the first line break should go, not the whole block.
            if (ManagerRichTextBox.Document.Blocks.FirstBlock is Paragraph firstParagraph)
            {
                string firstParagraphText = new TextRange(firstParagraph.ContentStart, firstParagraph.ContentEnd).Text;
                int firstNewline = firstParagraphText.IndexOf('\n');
                if (firstNewline < 0)
                {
                    ManagerRichTextBox.Document.Blocks.Remove(firstParagraph);
                }
                else
                {
                    string remainder = firstParagraphText.Substring(firstNewline + 1);
                    firstParagraph.Inlines.Clear();
                    firstParagraph.Inlines.Add(new Run(remainder));
                }
            }

            NoteContentHelper.RewireInteractiveElements(ManagerRichTextBox.Document, Hyperlink_RequestNavigate);

            TextRange initRange = new TextRange(ManagerRichTextBox.Document.ContentStart, ManagerRichTextBox.Document.ContentEnd);
            _lastHistoryContent = note.Content ?? "";
            _lastHistoryPlain = (note.Title ?? "") + "\n" + initRange.Text.Trim();
            _lastHistoryTime = DateTime.Now;

            RefreshEditorCategoryCombo();
            RefreshEditorTags();
            UpdateWordCount();

            RefreshNotesList();
            _isLoadingNote = false;
        }

        private void RefreshEditorCategoryCombo()
        {
            if (_currentNote == null) return;

            var allNotes = DatabaseHelper.ListNotes(null, null);
            var categories = allNotes.Select(n => n.Category ?? "General").Distinct().ToList();
            if (!categories.Contains(_currentNote.Category ?? "General")) categories.Add(_currentNote.Category ?? "General");
            if (!categories.Contains("General")) categories.Add("General");

            EditorCategoryCombo.SelectionChanged -= EditorCategoryCombo_SelectionChanged;
            EditorCategoryCombo.ItemsSource = categories.OrderBy(c => c == "General" ? "zzz" : c).ToList();
            EditorCategoryCombo.SelectedItem = _currentNote.Category ?? "General";
            EditorCategoryCombo.SelectionChanged += EditorCategoryCombo_SelectionChanged;
        }

        private void RefreshEditorTags()
        {
            if (_currentNote == null) return;
            EditorTagsItemsControl.ItemsSource = DatabaseHelper.GetNoteTags(_currentNote.Id);
        }

        private void EditorAddTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNote == null) return;
            int id = _currentNote.Id;

            var existingNoteTags = new HashSet<string>(DatabaseHelper.GetNoteTags(id), StringComparer.OrdinalIgnoreCase);
            var availableTags = DatabaseHelper.ListAllTags().Where(t => !existingNoteTags.Contains(t)).ToList();

            var menu = new ContextMenu();

            if (availableTags.Count > 0)
            {
                foreach (var tag in availableTags)
                {
                    var item = new MenuItem { Header = $"#{tag}" };
                    string tagCopy = tag;
                    item.Click += (s, args) =>
                    {
                        DatabaseHelper.AddTagToNote(id, tagCopy);
                        RefreshEditorTags();
                        _owner.RefreshTagsFilter();
                    };
                    menu.Items.Add(item);
                }
                menu.Items.Add(new Separator());
            }

            var newTagItem = new MenuItem { Header = "+ New Tag..." };
            newTagItem.Click += (s, args) =>
            {
                var dlg = new InputDialog("Enter tag name:", "Add Tag") { Owner = this };
                if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Answer))
                {
                    DatabaseHelper.AddTagToNote(id, dlg.Answer);
                    RefreshEditorTags();
                    _owner.RefreshTagsFilter();
                }
            };
            menu.Items.Add(newTagItem);

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private void EditorCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_currentNote == null || _isLoadingNote) return;
            if (EditorCategoryCombo.SelectedItem is not string category) return;

            _currentNote.Category = category;
            DatabaseHelper.UpdateNote(_currentNote);
            RefreshCategoryTabs();
            RefreshNotesList();
        }

        private void EditorFavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNote == null) return;
            _currentNote.IsFavorite = !_currentNote.IsFavorite;
            DatabaseHelper.SetFavorite(_currentNote.Id, _currentNote.IsFavorite);
            EditorFavoriteButton.Content = _currentNote.IsFavorite ? "★" : "☆";
            EditorFavoriteButton.Foreground = _currentNote.IsFavorite ? new SolidColorBrush(Color.FromRgb(0xff, 0xc1, 0x07)) : new SolidColorBrush(Color.FromArgb(0x80, 0xff, 0xff, 0xff));
            RefreshNotesList();
        }

        private void OpenInNoteWindowButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNote == null) return;
            _owner.OpenNoteWindow(_currentNote.Id);
        }

        private void DeleteNoteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNote == null) return;
            var ans = MessageBox.Show("Are you sure you want to delete this note?", "Delete Note", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (ans != MessageBoxResult.Yes) return;

            int deletedId = _currentNote.Id;
            DatabaseHelper.DeleteNote(deletedId);
            _currentNote = null;

            EmptyStateText.Visibility = Visibility.Visible;
            EditorHeaderGrid.Visibility = Visibility.Collapsed;
            EditorToolbar.Visibility = Visibility.Collapsed;
            ManagerRichTextBox.Visibility = Visibility.Collapsed;
            EditorFooterGrid.Visibility = Visibility.Collapsed;
            AiChatPanel.Visibility = Visibility.Collapsed;
            _isAiChatActive = false;

            RefreshCategoryTabs();
            RefreshNotesList();
        }

        private void ManagerRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingNote) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void EditorTitleBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isLoadingNote) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void UpdateWordCount()
        {
            string plainText = new TextRange(ManagerRichTextBox.Document.ContentStart, ManagerRichTextBox.Document.ContentEnd).Text;
            int wordCount = plainText.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Length;
            ManagerWordCountText.Text = wordCount == 0 ? "" : $"{wordCount} word{(wordCount == 1 ? "" : "s")}";
        }

        private void SaveCurrentNote()
        {
            if (_currentNote == null) return;

            string title = EditorTitleBox.Text.Trim();

            // The rest of the app stores a note's title as the first line of its content (see
            // NoteWindow.SaveNoteContent), so temporarily prepend a title paragraph before
            // serializing -- keeps the DB format consistent even though the editor here shows
            // title and body as separate, non-duplicated areas.
            var titleParagraph = new Paragraph(new Run(title));
            if (ManagerRichTextBox.Document.Blocks.FirstBlock != null)
                ManagerRichTextBox.Document.Blocks.InsertBefore(ManagerRichTextBox.Document.Blocks.FirstBlock, titleParagraph);
            else
                ManagerRichTextBox.Document.Blocks.Add(titleParagraph);

            TextRange range = new TextRange(ManagerRichTextBox.Document.ContentStart, ManagerRichTextBox.Document.ContentEnd);
            string xamlText = NoteContentHelper.SaveRange(range);
            string plainText = range.Text.Trim();

            ManagerRichTextBox.Document.Blocks.Remove(titleParagraph);

            _currentNote.Title = title;
            _currentNote.Content = xamlText;
            DatabaseHelper.UpdateNote(_currentNote);

            if (_lastHistoryContent != xamlText)
            {
                int diff = Math.Abs(plainText.Length - _lastHistoryPlain.Length);
                bool timeElapsed = (DateTime.Now - _lastHistoryTime).TotalSeconds >= 15;
                if (diff >= 15 || timeElapsed)
                {
                    if (!string.IsNullOrEmpty(_lastHistoryContent))
                    {
                        DatabaseHelper.AddNoteHistoryEntry(_currentNote.Id, _lastHistoryContent);
                    }
                    _lastHistoryContent = xamlText;
                    _lastHistoryPlain = plainText;
                    _lastHistoryTime = DateTime.Now;
                }
            }

            UpdateWordCount();
            _owner.RefreshNotesList();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); } catch { }
            e.Handled = true;
        }

        private void ManagerRichTextBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var position = ManagerRichTextBox.GetPositionFromPoint(e.GetPosition(ManagerRichTextBox), true);
            var checklistRun = NoteContentHelper.GetChecklistRunAt(position);
            if (checklistRun != null)
            {
                NoteContentHelper.ToggleChecklistItem(checklistRun);
                e.Handled = true;
            }
        }

        #endregion

        #region AI Features (Quick Format + Chat)

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

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private async Task ApplyAiFormatAsync(string promptPrefix)
        {
            if (_currentNote == null) return;
            TextRange range = new TextRange(ManagerRichTextBox.Document.ContentStart, ManagerRichTextBox.Document.ContentEnd);
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
                    ManagerRichTextBox.Document.Blocks.Clear();
                    ManagerRichTextBox.Document.Blocks.Add(new Paragraph(new Run(aiOutput)));
                    SaveCurrentNote();
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

        // Unlike the options above, this appends to the note rather than replacing it.
        private async Task ExtractActionItemsAsync()
        {
            if (_currentNote == null) return;
            TextRange range = new TextRange(ManagerRichTextBox.Document.ContentStart, ManagerRichTextBox.Document.ContentEnd);
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
                var tasks = AiHelper.ParseJsonStringArray(aiOutput).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();

                if (tasks.Count == 0)
                {
                    MessageBox.Show("No action items were found in this note.", "Extract Action Items", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var heading = new Paragraph(new Run("Action Items")) { FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 10, 0, 3) };
                ManagerRichTextBox.Document.Blocks.Add(heading);
                foreach (var task in tasks)
                {
                    ManagerRichTextBox.Document.Blocks.Add(new Paragraph(new Run(NoteContentHelper.UncheckedGlyph + task.Trim())));
                }

                SaveCurrentNote();
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

        private void AiChatToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentNote == null) return;
            _isAiChatActive = !_isAiChatActive;

            if (_isAiChatActive)
            {
                AiChatPanel.Visibility = Visibility.Visible;
                AiPromptTextBox.Focus();

                if (ChatHistoryPanel.Children.Count == 0)
                {
                    AddChatBubble("Hello! I am your AI assistant. You can ask me questions about this note.", false);
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

        private async Task ProcessAiChatQueryAsync()
        {
            string query = AiPromptTextBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            AiPromptTextBox.Text = "";
            AddChatBubble(query, true);
            ShowTypingIndicator();

            try
            {
                TextRange range = new TextRange(ManagerRichTextBox.Document.ContentStart, ManagerRichTextBox.Document.ContentEnd);
                string noteText = range.Text.Trim();

                string prompt = $"You are a helpful desktop note assistant. Answer the user's question contextually based on the note text provided below. Answer clearly and keep it short.\n\nNote Context:\n{noteText}\n\nUser Question:\n{query}";

                string aiResponse = await AiHelper.GenerateTextAsync(prompt);

                RemoveTypingIndicator();
                AddChatBubble(string.IsNullOrEmpty(aiResponse) ? "Sorry, I could not generate a response. Please check if Ollama is running." : aiResponse, false);
            }
            catch (Exception ex)
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
                MaxWidth = 320
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

        #endregion

        #region Formatting Toolbar

        private void FormatBold_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleBold.Execute(null, ManagerRichTextBox);
            ManagerRichTextBox.Focus();
        }

        private void FormatItalic_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleItalic.Execute(null, ManagerRichTextBox);
            ManagerRichTextBox.Focus();
        }

        private void FormatUnderline_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleUnderline.Execute(null, ManagerRichTextBox);
            ManagerRichTextBox.Focus();
        }

        private void FormatBullets_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleBullets.Execute(null, ManagerRichTextBox);
            ManagerRichTextBox.Focus();
        }

        private void FormatChecklist_Click(object sender, RoutedEventArgs e)
        {
            var caretParagraph = ManagerRichTextBox.CaretPosition.Paragraph;
            var newParagraph = new Paragraph();
            var run = new Run(NoteContentHelper.UncheckedGlyph + "New task");
            newParagraph.Inlines.Add(run);

            if (caretParagraph != null)
                ManagerRichTextBox.Document.Blocks.InsertAfter(caretParagraph, newParagraph);
            else
                ManagerRichTextBox.Document.Blocks.Add(newParagraph);

            var textStart = run.ContentStart.GetPositionAtOffset(NoteContentHelper.UncheckedGlyph.Length) ?? run.ContentStart;
            ManagerRichTextBox.Selection.Select(textStart, run.ContentEnd);
            ManagerRichTextBox.Focus();
        }

        private void FormatStrikethrough_Click(object sender, RoutedEventArgs e)
        {
            var range = ManagerRichTextBox.Selection;
            var currentDecoration = range.GetPropertyValue(Inline.TextDecorationsProperty);
            range.ApplyPropertyValue(Inline.TextDecorationsProperty, currentDecoration == TextDecorations.Strikethrough ? null : TextDecorations.Strikethrough);
            ManagerRichTextBox.Focus();
        }

        private void FormatHighlight_Click(object sender, RoutedEventArgs e)
        {
            var range = ManagerRichTextBox.Selection;
            var currentBackground = range.GetPropertyValue(TextElement.BackgroundProperty);

            var yellowBrush = new SolidColorBrush(Color.FromArgb(80, 255, 235, 59));
            if (currentBackground is SolidColorBrush brush && brush.Color == yellowBrush.Color)
                range.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            else
                range.ApplyPropertyValue(TextElement.BackgroundProperty, yellowBrush);
            ManagerRichTextBox.Focus();
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
                var swatch = new Border { Width = 13, Height = 13, Background = new SolidColorBrush(color), CornerRadius = new CornerRadius(3), Margin = new Thickness(0, 0, 8, 0) };
                var stack = new StackPanel { Orientation = Orientation.Horizontal };
                stack.Children.Add(swatch);
                stack.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });

                var item = new MenuItem { Header = stack };
                var capturedColor = color;
                item.Click += (s, args) =>
                {
                    if (!ManagerRichTextBox.Selection.IsEmpty)
                        ManagerRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(capturedColor));
                    ManagerRichTextBox.Focus();
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
            if (ManagerRichTextBox.Selection.IsEmpty) return;

            double current = ManagerRichTextBox.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size ? size : 14.0;
            double next = Math.Max(8.0, Math.Min(48.0, current + delta));
            ManagerRichTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, next);
            ManagerRichTextBox.Focus();
        }

        private void FormatHeading1_Click(object sender, RoutedEventArgs e) => ToggleHeading(18.0);

        private void FormatHeading2_Click(object sender, RoutedEventArgs e) => ToggleHeading(15.0);

        private void ToggleHeading(double targetSize)
        {
            var paragraph = ManagerRichTextBox.Selection.Start.Paragraph ?? ManagerRichTextBox.CaretPosition.Paragraph;
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
            ManagerRichTextBox.Focus();
        }

        private void FormatBlockquote_Click(object sender, RoutedEventArgs e)
        {
            var paragraph = ManagerRichTextBox.Selection.Start.Paragraph ?? ManagerRichTextBox.CaretPosition.Paragraph;
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
            ManagerRichTextBox.Focus();
        }

        private void FormatCodeBlock_Click(object sender, RoutedEventArgs e)
        {
            var codeFont = new FontFamily("Consolas");
            var codeBackground = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
            var codeForeground = new SolidColorBrush(Color.FromRgb(0xa8, 0xff, 0x78));

            if (!ManagerRichTextBox.Selection.IsEmpty)
            {
                var selection = ManagerRichTextBox.Selection;
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

                var caretParagraph = ManagerRichTextBox.CaretPosition.Paragraph;
                if (caretParagraph != null)
                    ManagerRichTextBox.Document.Blocks.InsertAfter(caretParagraph, paragraph);
                else
                    ManagerRichTextBox.Document.Blocks.Add(paragraph);

                ManagerRichTextBox.CaretPosition = paragraph.ContentStart;
            }
            ManagerRichTextBox.Focus();
        }

        private void FormatNumberedList_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleNumbering.Execute(null, ManagerRichTextBox);
            ManagerRichTextBox.Focus();
        }

        private void FormatIndent_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.IncreaseIndentation.Execute(null, ManagerRichTextBox);
            ManagerRichTextBox.Focus();
        }

        private void FormatOutdent_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.DecreaseIndentation.Execute(null, ManagerRichTextBox);
            ManagerRichTextBox.Focus();
        }

        private void InsertDivider_Click(object sender, RoutedEventArgs e)
        {
            var divider = new Paragraph(new Run(""))
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 10, 0, 10)
            };

            var caretParagraph = ManagerRichTextBox.CaretPosition.Paragraph;
            if (caretParagraph != null)
                ManagerRichTextBox.Document.Blocks.InsertAfter(caretParagraph, divider);
            else
                ManagerRichTextBox.Document.Blocks.Add(divider);

            var following = new Paragraph(new Run(""));
            ManagerRichTextBox.Document.Blocks.InsertAfter(divider, following);
            ManagerRichTextBox.CaretPosition = following.ContentStart;

            ManagerRichTextBox.Focus();
        }

        private void FormatClear_Click(object sender, RoutedEventArgs e)
        {
            if (ManagerRichTextBox.Selection.IsEmpty) return;

            var selection = ManagerRichTextBox.Selection;
            selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
            selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
            selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"));
            selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White);
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            ManagerRichTextBox.Focus();
        }

        private void InsertHyperlink_Click(object sender, RoutedEventArgs e)
        {
            string selectedText = ManagerRichTextBox.Selection.Text;

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

            if (!ManagerRichTextBox.Selection.IsEmpty)
                ManagerRichTextBox.Selection.Text = string.Empty;

            var paragraph = ManagerRichTextBox.CaretPosition.Paragraph;
            if (paragraph == null)
            {
                paragraph = new Paragraph();
                ManagerRichTextBox.Document.Blocks.Add(paragraph);
            }

            var hyperlink = new Hyperlink(new Run(displayText))
            {
                NavigateUri = uri,
                Foreground = new SolidColorBrush(Color.FromRgb(0x4d, 0xb8, 0xff))
            };
            hyperlink.RequestNavigate += Hyperlink_RequestNavigate;
            paragraph.Inlines.Add(hyperlink);

            ManagerRichTextBox.Focus();
        }

        #endregion
    }
}
