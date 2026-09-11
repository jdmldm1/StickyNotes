using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Threading;

namespace StickyNotes__
{
    public partial class SpotlightWindow : Window
    {
        private readonly MainWindow _mainWnd;
        private DispatcherTimer? _searchDebounceTimer;

        public SpotlightWindow(MainWindow mainWnd)
        {
            InitializeComponent();
            _mainWnd = mainWnd;
        }

        private bool _isPopulatingFilters;
        private const string AllTagsOption = "All Tags";
        private const string AllCategoriesOption = "All Categories";

        private static readonly List<(string Command, string Description, string Shortcut)> Commands = new()
        {
            ("/new",      "Create a new blank note",              "Win+Alt+N"),
            ("/template", "Create a note from a template",        "─"),
            ("/graph",    "Open the Tag Mind Graph",              "Win+Alt+G"),
            ("/manager",  "Open the Note Manager",               "─"),
            ("/teams",    "Microsoft Teams Meetings & Transcripts", "─"),
            ("/kanban",   "Open Visual Kanban Board",            "Win+Alt+K"),
            ("/hud",      "Open In-Meeting Floating HUD",        "─"),
            ("/scratchpad", "Open Ghost Scratchpad",             "Win+Shift+N"),
            ("/snip",     "Take a region screenshot",            "Win+Alt+S"),
            ("/capture",  "Open Quick Capture",                  "Win+Alt+Q"),
            ("/clipboard", "Open Clipboard History",             "─"),
        };

        public void FocusSearch()
        {
            SearchInput.Text = "";
            ResultsListBox.ItemsSource = null;
            PopulateFilters();
            SearchInput.Focus();
        }

        private void PopulateFilters()
        {
            _isPopulatingFilters = true;
            try
            {
                var tags = new List<string> { AllTagsOption };
                tags.AddRange(DatabaseHelper.ListAllTags());
                TagFilterCombo.ItemsSource = tags;
                TagFilterCombo.SelectedIndex = 0;

                var categories = new List<string> { AllCategoriesOption };
                categories.AddRange(DatabaseHelper.ListAllCategories());
                CategoryFilterCombo.ItemsSource = categories;
                CategoryFilterCombo.SelectedIndex = 0;

                TimeFilterCombo.ItemsSource = new[] { "Any Time", "Today", "This Week", "This Month" };
                TimeFilterCombo.SelectedIndex = 0;
            }
            finally
            {
                _isPopulatingFilters = false;
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FocusSearch();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                _searchDebounceTimer.Tick += (s, args) =>
                {
                    _searchDebounceTimer!.Stop();
                    RunSearch();
                };
            }
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        private void OmnibarChip_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string cmd)
            {
                SearchInput.Text = cmd;
                SearchInput.CaretIndex = SearchInput.Text.Length;
                SearchInput.Focus();
            }
        }

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingFilters) return;
            RunSearch();
        }

        private void RunSearch()
        {
            string query = SearchInput.Text.Trim();
            var results = new List<NoteCardViewModel>();

            if (TryEvaluateMath(query, out string mathResult))
            {
                results.Add(new NoteCardViewModel
                {
                    Id = -999,
                    Title = $"= {mathResult}",
                    Color = "green",
                    Snippet = $"Calculated from \"{query.TrimStart('=').Trim()}\" • Press Enter to copy result",
                });
            }

            if (query.StartsWith("/todo ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("/task ", StringComparison.OrdinalIgnoreCase))
            {
                int firstSpace = query.IndexOf(' ');
                string taskText = firstSpace >= 0 ? query.Substring(firstSpace + 1).Trim() : "";
                if (!string.IsNullOrEmpty(taskText))
                {
                    results.Add(new NoteCardViewModel
                    {
                        Id = -998,
                        Title = $"➕ Add Task: {taskText}",
                        Color = "blue",
                        Snippet = "Appends checklist item to your latest active note • Press Enter",
                    });
                }
            }

            if (query.StartsWith("/new ", StringComparison.OrdinalIgnoreCase) || query.StartsWith("/n ", StringComparison.OrdinalIgnoreCase))
            {
                int firstSpace = query.IndexOf(' ');
                string noteTitle = firstSpace >= 0 ? query.Substring(firstSpace + 1).Trim() : "";
                if (!string.IsNullOrEmpty(noteTitle))
                {
                    results.Add(new NoteCardViewModel
                    {
                        Id = -997,
                        Title = $"📝 Create Note: \"{noteTitle}\"",
                        Color = "yellow",
                        Snippet = "Creates and opens a new note with this title • Press Enter",
                    });
                }
            }

            if (query.StartsWith("/") && !query.StartsWith("/todo ", StringComparison.OrdinalIgnoreCase) && !query.StartsWith("/task ", StringComparison.OrdinalIgnoreCase) && !query.StartsWith("/new ", StringComparison.OrdinalIgnoreCase) && !query.StartsWith("/n ", StringComparison.OrdinalIgnoreCase))
            {
                string q = query.ToLowerInvariant();
                var matched = Commands
                    .Where(c => c.Command.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                    .Select(c => new NoteCardViewModel
                    {
                        Id = -1,
                        Title = c.Command,
                        Color = "blue",
                        Snippet = c.Description + (c.Shortcut != "─" ? $"   [{c.Shortcut}]" : ""),
                    });
                results.AddRange(matched);
                ResultsListBox.ItemsSource = results;
                if (results.Count > 0) ResultsListBox.SelectedIndex = 0;
                return;
            }

            string? tagFilter = TagFilterCombo.SelectedItem as string;
            if (tagFilter == AllTagsOption) tagFilter = null;

            string? categoryFilter = CategoryFilterCombo.SelectedItem as string;
            if (categoryFilter == AllCategoriesOption) categoryFilter = null;

            DateTime? updatedSince = (TimeFilterCombo.SelectedItem as string) switch
            {
                "Today" => DateTime.Today,
                "This Week" => DateTime.Today.AddDays(-(int)DateTime.Today.DayOfWeek),
                "This Month" => new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1),
                _ => null
            };

            if (string.IsNullOrEmpty(query) && tagFilter == null && categoryFilter == null && updatedSince == null)
            {
                ResultsListBox.ItemsSource = results.Count > 0 ? results : null;
                if (results.Count > 0) ResultsListBox.SelectedIndex = 0;
                return;
            }

            var notes = DatabaseHelper.SearchNotesFts(query ?? "", tagFilter, categoryFilter, updatedSince);
            foreach (var n in notes)
            {
                var vm = new NoteCardViewModel
                {
                    Id = n.Id,
                    Title = n.Title,
                    Color = n.Color,
                    Snippet = BuildContextSnippet(n.PlainText, query ?? ""),
                    ImagePath = n.ImagePath,
                    Category = n.Category ?? "General",
                    IsFavorite = n.IsFavorite,
                    IsSecure = n.IsSecure,
                    UpdatedAt = n.UpdatedAt
                };
                vm.InitComputedProperties();
                results.Add(vm);
            }

            ResultsListBox.ItemsSource = results;
            if (results.Count > 0)
            {
                ResultsListBox.SelectedIndex = 0;
            }
        }

        private static bool TryEvaluateMath(string text, out string result)
        {
            result = "";
            text = text.Trim();
            if (text.StartsWith("=")) text = text.Substring(1).Trim();
            else if (text.StartsWith("calc:", StringComparison.OrdinalIgnoreCase)) text = text.Substring(5).Trim();
            else
            {
                if (text.Length < 3) return false;

                if (!text.Any(c => c == '+' || c == '*' || c == '/' || (c == '-' && text.IndexOf('-') > 0)))
                    return false;
                if (text.Any(c => char.IsLetter(c)))
                    return false;
            }

            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                string expr = text.Replace("×", "*").Replace("÷", "/");
                var dt = new System.Data.DataTable();
                var computeResult = dt.Compute(expr, null);
                if (computeResult != null && computeResult != DBNull.Value)
                {
                    double val = Convert.ToDouble(computeResult);
                    result = val.ToString("G15");
                    return true;
                }
            }
            catch { }

            return false;
        }

        private void AddTodoToLatestNote(string taskText)
        {
            try
            {
                var notes = DatabaseHelper.ListNotes(includeContent: true).Where(n => !n.IsSecure).ToList();
                Note? target = notes.FirstOrDefault();
                if (target == null)
                {
                    int id = DatabaseHelper.CreateNote("Tasks", "", null, null, "yellow");
                    target = DatabaseHelper.GetNote(id);
                }

                if (target != null)
                {
                    string line = $"- [ ] {taskText}";
                    var doc = new FlowDocument();
                    if (!string.IsNullOrEmpty(target.Content))
                    {
                        NoteContentHelper.TryLoadRange(new TextRange(doc.ContentStart, doc.ContentEnd), target.Content);
                    }
                    doc.Blocks.Add(new Paragraph(new Run(line)));
                    TextRange tr = new TextRange(doc.ContentStart, doc.ContentEnd);
                    target.Content = NoteContentHelper.SaveRange(tr);
                    target.PlainText = tr.Text;
                    DatabaseHelper.UpdateNote(target);
                    _mainWnd.QueueRefreshNotesList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("AddTodoToLatestNote error: " + ex.Message);
            }
        }

        private static string BuildContextSnippet(string plainText, string query, int contextChars = 60)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            if (string.IsNullOrEmpty(query)) return Truncate(plainText, contextChars * 2);

            int matchIndex = plainText.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (matchIndex < 0) return Truncate(plainText, contextChars * 2);

            int start = Math.Max(0, matchIndex - contextChars);
            int end = Math.Min(plainText.Length, matchIndex + query.Length + contextChars);
            string snippet = plainText.Substring(start, end - start).Replace('\n', ' ').Replace('\r', ' ').Trim();

            if (start > 0) snippet = "…" + snippet;
            if (end < plainText.Length) snippet += "…";
            return snippet;
        }

        private static string Truncate(string text, int maxLength)
        {
            text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return text.Length > maxLength ? text.Substring(0, maxLength) + "…" : text;
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
                if (_searchDebounceTimer != null && _searchDebounceTimer.IsEnabled)
                {
                    _searchDebounceTimer.Stop();
                    RunSearch();
                }
                OpenSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                int nextIndex = ResultsListBox.SelectedIndex + 1;
                if (nextIndex < ResultsListBox.Items.Count)
                {
                    ResultsListBox.SelectedIndex = nextIndex;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                int prevIndex = ResultsListBox.SelectedIndex - 1;
                if (prevIndex >= 0)
                {
                    ResultsListBox.SelectedIndex = prevIndex;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }
                e.Handled = true;
            }
        }

        private void RunCommandSearch(string query)
        {
            string q = query.ToLowerInvariant();
            var matched = Commands
                .Where(c => c.Command.StartsWith(q, StringComparison.OrdinalIgnoreCase))
                .Select(c => new NoteCardViewModel
                {
                    Id = -1,
                    Title = c.Command,
                    Color = "blue",
                    Snippet = c.Description + (c.Shortcut != "─" ? $"   [{c.Shortcut}]" : ""),
                })
                .ToList();

            ResultsListBox.ItemsSource = matched;
            if (matched.Count > 0) ResultsListBox.SelectedIndex = 0;
        }

        private void ExecuteCommand(string command)
        {
            this.Hide();
            string cmd = command.Split(' ')[0].ToLowerInvariant();
            switch (cmd)
            {
                case "/new":
                    _mainWnd.CreateNewNote();
                    break;
                case "/template":
                    _mainWnd.OpenTemplatePicker();
                    break;
                case "/graph":
                    _mainWnd.OpenGraphWindow();
                    break;
                case "/manager":
                    _mainWnd.OpenNoteManager();
                    break;
                case "/teams":
                    _mainWnd.OpenTeamsMeetingWindow();
                    break;
                case "/kanban":
                    _mainWnd.OpenKanbanWindow();
                    break;
                case "/hud":
                    _mainWnd.OpenMeetingHudWindow();
                    break;
                case "/scratchpad":
                    _mainWnd.OpenGhostScratchpad();
                    break;
                case "/snip":
                    _mainWnd.TakeRegionScreenshot();
                    break;
                case "/capture":
                    _mainWnd.ToggleQuickCapture();
                    break;
                case "/clipboard":
                    _mainWnd.OpenClipboardPicker();
                    break;
            }
        }

        private void OpenSelected()
        {
            if (ResultsListBox.SelectedItem is NoteCardViewModel vm)
            {
                if (vm.Id == -999)
                {
                    string copyVal = vm.Title.TrimStart('=', ' ').Trim();
                    try { Clipboard.SetText(copyVal); } catch {}
                    this.Hide();
                    return;
                }
                if (vm.Id == -998)
                {
                    string taskText = vm.Title.Replace("➕ Add Task:", "").Trim();
                    AddTodoToLatestNote(taskText);
                    this.Hide();
                    return;
                }
                if (vm.Id == -997)
                {
                    string noteTitle = vm.Title.Replace("📝 Create Note:", "").Trim(' ', '"');
                    this.Hide();
                    int newId = DatabaseHelper.CreateNote(noteTitle, "", null, null, "yellow");
                    _mainWnd.OpenNoteWindow(newId);
                    return;
                }
                if (vm.Id == -1)
                {
                    ExecuteCommand(vm.Title);
                    return;
                }
                this.Hide();
                _mainWnd.OpenNoteWindow(vm.Id);
            }
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelected();
        }
    }
}
