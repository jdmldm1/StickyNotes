using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StickyNotes__
{
    public partial class SpotlightWindow : Window
    {
        private readonly MainWindow _mainWnd;

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
            ("/snip",     "Take a region screenshot",            "Win+Alt+S"),
            ("/capture",  "Open Quick Capture",                  "Win+Alt+Q"),
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
                categories.AddRange(DatabaseHelper.ListNotes(null, null).Select(n => n.Category ?? "General").Distinct().OrderBy(c => c));
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

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e) => RunSearch();

        private void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isPopulatingFilters) return;
            RunSearch();
        }

        private void RunSearch()
        {
            string query = SearchInput.Text.Trim();

            if (query.StartsWith("/"))
            {
                RunCommandSearch(query);
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
                ResultsListBox.ItemsSource = null;
                return;
            }

            var notes = DatabaseHelper.ListNotes(string.IsNullOrEmpty(query) ? null : query, tagFilter, categoryFilter, updatedSince);
            var viewModels = notes.Select(n => new NoteCardViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Color = n.Color,
                Snippet = BuildContextSnippet(NoteContentHelper.ExtractPlainText(n.Content), query),
                ImagePath = n.ImagePath
            }).ToList();

            ResultsListBox.ItemsSource = viewModels;
            if (viewModels.Count > 0)
            {
                ResultsListBox.SelectedIndex = 0;
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
                case "/snip":
                    _mainWnd.TakeRegionScreenshot();
                    break;
                case "/capture":
                    _mainWnd.ToggleQuickCapture();
                    break;
            }
        }

        private void OpenSelected()
        {
            if (ResultsListBox.SelectedItem is NoteCardViewModel vm)
            {
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
