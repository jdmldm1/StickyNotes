using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace StickyNotes__
{
    public partial class MainWindow : Window
    {
        private string? _selectedTagFilter;
        private DateTime? _filterExactDate;
        private readonly HashSet<string> _selectedTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _tagMatrixModeAll;
        private List<int> _recentNoteIds = new List<int>();
        private bool _filterFavoritesOnly;
        private bool _filterHasTasksOnly;
        private bool _filterHasFilesOnly;
        private string? _filterColor;
        private string _sortOrder = "date";
        private string _cardSize = "Medium";
        private DispatcherTimer? _searchDebounceTimer;
        private DispatcherTimer? _refreshDebounceTimer;
        private bool _isNotesListDirty;

        private void DebounceSearch()
        {
            if (_searchDebounceTimer == null)
            {
                _searchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                _searchDebounceTimer.Tick += (s, e) =>
                {
                    _searchDebounceTimer!.Stop();
                    RefreshNotesList();
                };
            }
            _searchDebounceTimer.Stop();
            _searchDebounceTimer.Start();
        }

        public void QueueRefreshNotesList()
        {
            if (this.Visibility != Visibility.Visible)
            {
                _isNotesListDirty = true;
                return;
            }

            if (_refreshDebounceTimer == null)
            {
                _refreshDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
                _refreshDebounceTimer.Tick += (s, e) =>
                {
                    _refreshDebounceTimer!.Stop();
                    RefreshNotesList();
                };
            }
            _refreshDebounceTimer.Stop();
            _refreshDebounceTimer.Start();
        }

        private static readonly Brush FavoriteGroupBrush = NoteCardViewModel.FrozenBrush(255, 0xff, 0xc1, 0x07);
        private static readonly Brush NotesGroupBrush = NoteCardViewModel.FrozenBrush(255, 0x00, 0x84, 0xff);
        private static readonly Brush TodayBrush = NoteCardViewModel.FrozenBrush(255, 0x00, 0xd2, 0xff);
        private static readonly Brush YesterdayBrush = NoteCardViewModel.FrozenBrush(255, 0x4f, 0xc3, 0xf7);
        private static readonly Brush WeekBrush = NoteCardViewModel.FrozenBrush(255, 0x26, 0xa6, 0x9a);
        private static readonly Brush MonthBrush = NoteCardViewModel.FrozenBrush(255, 0xab, 0x47, 0xbc);
        private static readonly Brush OlderBrush = NoteCardViewModel.FrozenBrush(255, 0x90, 0xa4, 0xae);
        private static readonly string[] CategoryColors = { "#D49A13", "#1A8F54", "#C2185B", "#7B1FA2", "#0288D1", "#e65100" };
        private static readonly Dictionary<string, Brush> _categoryBrushCache = new Dictionary<string, Brush>(StringComparer.OrdinalIgnoreCase);

        private static Brush GetCategoryColorBrush(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName) || categoryName == "General")
                return Brushes.White;

            if (_categoryBrushCache.TryGetValue(categoryName, out var cached))
                return cached;

            string? customHex = DatabaseHelper.GetCategoryColor(categoryName);
            var converter = new System.Windows.Media.BrushConverter();
            if (!string.IsNullOrEmpty(customHex))
            {
                try
                {
                    var b = (Brush)converter.ConvertFromString(customHex)!;
                    b.Freeze();
                    _categoryBrushCache[categoryName] = b;
                    return b;
                }
                catch {}
            }

            int hash = 0;
            foreach (char c in categoryName)
                hash = hash * 31 + c;

            int idx = Math.Abs(hash) % CategoryColors.Length;
            var fallback = (Brush)converter.ConvertFromString(CategoryColors[idx])!;
            fallback.Freeze();
            _categoryBrushCache[categoryName] = fallback;
            return fallback;
        }

        public ContextMenu CreateCategoryContextMenu(string categoryName, Action onChanged)
        {
            var menu = new ContextMenu();

            var colors = new[]
            {
                ("Blue", "#0288D1"),
                ("Green", "#1A8F54"),
                ("Purple", "#7B1FA2"),
                ("Pink", "#C2185B"),
                ("Yellow", "#D49A13"),
                ("Orange", "#e65100"),
                ("Red", "#d32f2f"),
                ("Gray", "#888888")
            };

            var converter = new System.Windows.Media.BrushConverter();
            foreach (var (name, hex) in colors)
            {
                var item = new MenuItem { Header = name };

                item.Icon = new System.Windows.Shapes.Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = (Brush)converter.ConvertFromString(hex)!,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };

                item.Click += (s, e) =>
                {
                    DatabaseHelper.SetCategoryColor(categoryName, hex);
                    _categoryBrushCache.Remove(categoryName);
                    onChanged();
                };
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());

            var resetItem = new MenuItem { Header = "Reset to Default" };
            resetItem.Icon = new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Stroke = Brushes.White,
                StrokeThickness = 1,
                Fill = Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            resetItem.Click += (s, e) =>
            {
                DatabaseHelper.ResetCategoryColor(categoryName);
                _categoryBrushCache.Remove(categoryName);
                onChanged();
            };
            menu.Items.Add(resetItem);

            return menu;
        }
        private static readonly Dictionary<string, bool> _expanderStates = new Dictionary<string, bool>();
        public void RefreshNotesList()
        {
            if (NotesListBox == null) return;

            if (this.Visibility != Visibility.Visible)
            {
                _isNotesListDirty = true;
                return;
            }
            _isNotesListDirty = false;

            string rawSearch = SearchTextBox?.Text?.Trim() ?? "";
            string effectiveSearch = rawSearch;
            string? syntaxTag = null;
            string? syntaxCategory = null;
            string? syntaxColor = null;
            bool syntaxTasks = false;
            bool syntaxFavorites = false;
            bool syntaxFiles = false;

            if (!string.IsNullOrEmpty(rawSearch))
            {
                var tokens = rawSearch.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                var remainingTokens = new List<string>();
                foreach (var token in tokens)
                {
                    if (token.StartsWith("#") && token.Length > 1)
                    {
                        syntaxTag = token.Substring(1);
                    }
                    else if (token.StartsWith("tag:", StringComparison.OrdinalIgnoreCase) && token.Length > 4)
                    {
                        syntaxTag = token.Substring(4);
                    }
                    else if (token.StartsWith("cat:", StringComparison.OrdinalIgnoreCase) && token.Length > 4)
                    {
                        syntaxCategory = token.Substring(4);
                    }
                    else if (token.StartsWith("color:", StringComparison.OrdinalIgnoreCase) && token.Length > 6)
                    {
                        syntaxColor = token.Substring(6);
                    }
                    else if (token.Equals("is:todo", StringComparison.OrdinalIgnoreCase) || token.Equals("has:task", StringComparison.OrdinalIgnoreCase) || token.Equals("is:task", StringComparison.OrdinalIgnoreCase))
                    {
                        syntaxTasks = true;
                    }
                    else if (token.Equals("is:fav", StringComparison.OrdinalIgnoreCase) || token.Equals("is:pinned", StringComparison.OrdinalIgnoreCase))
                    {
                        syntaxFavorites = true;
                    }
                    else if (token.Equals("is:file", StringComparison.OrdinalIgnoreCase) || token.Equals("has:file", StringComparison.OrdinalIgnoreCase) || token.Equals("has:link", StringComparison.OrdinalIgnoreCase))
                    {
                        syntaxFiles = true;
                    }
                    else
                    {
                        remainingTokens.Add(token);
                    }
                }
                effectiveSearch = string.Join(" ", remainingTokens);
            }

            string? activeTag = syntaxTag ?? _selectedTagFilter;
            string? activeColor = syntaxColor ?? _filterColor;
            bool activeFavorites = syntaxFavorites || _filterFavoritesOnly;
            bool activeTasks = syntaxTasks || _filterHasTasksOnly;
            bool activeFiles = syntaxFiles || _filterHasFilesOnly;

            var notes = DatabaseHelper.ListNotes(
                string.IsNullOrEmpty(effectiveSearch) ? null : effectiveSearch,
                activeTag,
                syntaxCategory
            );

            var attachmentsMap = DatabaseHelper.GetAllNoteAttachmentsMap();

            if (activeFavorites)
            {
                notes = notes.Where(n => n.IsFavorite).ToList();
            }

            if (!string.IsNullOrEmpty(activeColor))
            {
                notes = notes.Where(n => string.Equals(n.Color, activeColor, StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (activeTasks)
            {
                notes = notes.Where(n => n.PlainText.Contains("- [ ]") || n.PlainText.Contains("- [x]") || n.PlainText.Contains("- [X]")).ToList();
            }

            if (activeFiles)
            {
                notes = notes.Where(n => (attachmentsMap.TryGetValue(n.Id, out var atts) && atts.Count > 0) ||
                                         n.PlainText.Contains("http://", StringComparison.OrdinalIgnoreCase) ||
                                         n.PlainText.Contains("https://", StringComparison.OrdinalIgnoreCase)).ToList();
            }

            if (_showOnlyStale)
            {
                notes = notes.Where(IsStaleNote).ToList();
            }

            if (_filterExactDate.HasValue)
            {
                notes = notes.Where(n => n.UpdatedAt.Date == _filterExactDate.Value.Date).ToList();
            }

            var tagsMap = DatabaseHelper.GetAllNoteTagsMap();

            if (_selectedTags.Count > 0)
            {
                if (_tagMatrixModeAll)
                {
                    notes = notes.Where(n => tagsMap.TryGetValue(n.Id, out var nt) && _selectedTags.All(st => nt.Contains(st, StringComparer.OrdinalIgnoreCase))).ToList();
                }
                else
                {
                    notes = notes.Where(n => tagsMap.TryGetValue(n.Id, out var nt) && _selectedTags.Any(st => nt.Contains(st, StringComparer.OrdinalIgnoreCase))).ToList();
                }
            }

            var sortedNotes = notes.OrderByDescending(n => n.IsFavorite).ThenByDescending(n => n.UpdatedAt).ToList();

            var viewModels = sortedNotes.Select(n =>
            {
                string fullText = n.PlainText;
                List<string> tags = tagsMap.TryGetValue(n.Id, out var t) ? t : new List<string>();
                List<NoteAttachment> attachments = attachmentsMap.TryGetValue(n.Id, out var a) ? a : new List<NoteAttachment>();
                var vm = new NoteCardViewModel
                {
                    Id = n.Id,
                    Title = n.Title,
                    Color = n.Color,
                    Snippet = n.IsSecure ? "Protected — click to unlock" : BuildCardSnippet(fullText),
                    FullPlainText = fullText,
                    ImagePath = n.IsSecure ? null : n.ImagePath,
                    Tags = tags,
                    Category = n.Category ?? "General",
                    IsFavorite = n.IsFavorite,
                    IsSecure = n.IsSecure,
                    UpdatedAt = n.UpdatedAt,
                    CardSize = _cardSize,
                    Attachments = n.IsSecure ? new List<NoteAttachment>() : attachments,
                    HasLikelyLink = !n.IsSecure && (fullText.Contains("http://", StringComparison.OrdinalIgnoreCase) || fullText.Contains("https://", StringComparison.OrdinalIgnoreCase))
                };
                vm.InitComputedProperties();
                return vm;
            }).ToList();

            if (ClearSearchButton != null)
            {
                ClearSearchButton.Visibility = string.IsNullOrEmpty(rawSearch) ? Visibility.Collapsed : Visibility.Visible;
            }

            if (_cardSize == "Tasks")
            {
                var allTasks = new List<GlobalTaskItemViewModel>();
                foreach (var vm in viewModels)
                {
                    allTasks.AddRange(ExtractTasksFromNote(vm));
                }

                var openTasks = allTasks.Where(t => !t.IsCompleted).ToList();
                var doneTasks = allTasks.Where(t => t.IsCompleted).ToList();

                if (ResultCountTextBlock != null)
                {
                    ResultCountTextBlock.Text = $"{openTasks.Count} open, {doneTasks.Count} done";
                }

                var taskRows = new List<object>();
                var todoBrush = NoteCardViewModel.FrozenBrush(255, 0x00, 0xd2, 0xff);
                var doneBrush = NoteCardViewModel.FrozenBrush(255, 0x26, 0xa6, 0x9a);

                if (openTasks.Count > 0)
                    taskRows.AddRange(BuildTaskGroupRows("📋 To Do", todoBrush, openTasks, "__tasks_todo__"));
                if (doneTasks.Count > 0)
                    taskRows.AddRange(BuildTaskGroupRows("✓ Completed", doneBrush, doneTasks, "__tasks_completed__"));

                NotesListBox.ItemsSource = taskRows;
                UpdateExpandCollapseAllButtonLabel();
                return;
            }

            if (ResultCountTextBlock != null)
            {
                bool isFilterActive = !string.IsNullOrEmpty(rawSearch) || activeFavorites || activeTasks || activeFiles || !string.IsNullOrEmpty(activeColor) || !string.IsNullOrEmpty(activeTag) || _showOnlyStale || _filterExactDate.HasValue || _selectedTags.Count > 0;
                if (isFilterActive)
                {
                    int totalCount = DatabaseHelper.GetNoteCount();
                    ResultCountTextBlock.Text = $"{viewModels.Count} of {totalCount}";
                }
                else
                {
                    ResultCountTextBlock.Text = $"{viewModels.Count} note{(viewModels.Count == 1 ? "" : "s")}";
                }
            }

            var favoriteBrush = FavoriteGroupBrush;
            var notesBrush = NotesGroupBrush;

            var rows = new List<object>();

            if (_sortOrder == "category")
            {
                var favorites = viewModels.Where(vm => vm.IsFavorite).ToList();
                if (favorites.Count > 0)
                {
                    rows.AddRange(BuildGroupRows("★ Favorites", favoriteBrush, favorites, "__favorites__"));
                }

                var groups = viewModels
                    .GroupBy(vm => vm.Category)
                    .OrderBy(g => g.Key == "General" ? 1 : 0)
                    .ThenBy(g => g.Key)
                    .ToList();

                foreach (var group in groups)
                {
                    string categoryName = group.Key;
                    var groupItems = group.ToList();
                    var catBrush = GetCategoryColorBrush(categoryName);
                    string currentCatName = categoryName;

                    var contextMenu = CreateCategoryContextMenu(categoryName, () =>
                    {
                        RefreshNotesList();
                        _noteManagerWnd?.RefreshCategoryTabs();
                    });

                    rows.AddRange(BuildGroupRows(categoryName, catBrush, groupItems, categoryName,
                        contextMenu, () => CreateNewNote(currentCatName), $"Add new note to {categoryName}"));
                }
            }
            else
            {
                var favorites = viewModels.Where(vm => vm.IsFavorite).ToList();
                var rest = viewModels.Where(vm => !vm.IsFavorite).ToList();

                if (favorites.Count > 0)
                {
                    rows.AddRange(BuildGroupRows("★ Pinned", favoriteBrush, favorites, "__favorites__"));
                }

                var today = DateTime.Today;
                var todayItems = rest.Where(vm => vm.UpdatedAt.Date >= today).ToList();
                var yesterdayItems = rest.Where(vm => vm.UpdatedAt.Date == today.AddDays(-1)).ToList();
                var weekItems = rest.Where(vm => vm.UpdatedAt.Date >= today.AddDays(-7) && vm.UpdatedAt.Date < today.AddDays(-1)).ToList();
                var monthItems = rest.Where(vm => vm.UpdatedAt.Date >= today.AddDays(-30) && vm.UpdatedAt.Date < today.AddDays(-7)).ToList();
                var olderItems = rest.Where(vm => vm.UpdatedAt.Date < today.AddDays(-30)).ToList();

                if (todayItems.Count > 0)
                    rows.AddRange(BuildGroupRows("☀️ Today", TodayBrush, todayItems, "__timeline_today__"));
                if (yesterdayItems.Count > 0)
                    rows.AddRange(BuildGroupRows("📅 Yesterday", YesterdayBrush, yesterdayItems, "__timeline_yesterday__"));
                if (weekItems.Count > 0)
                    rows.AddRange(BuildGroupRows("🗓 Last 7 Days", WeekBrush, weekItems, "__timeline_week__"));
                if (monthItems.Count > 0)
                    rows.AddRange(BuildGroupRows("🗓 Earlier this Month", MonthBrush, monthItems, "__timeline_month__"));
                if (olderItems.Count > 0)
                    rows.AddRange(BuildGroupRows("🗄 Older", OlderBrush, olderItems, "__timeline_older__"));
            }

            NotesListBox.ItemsSource = rows;
            UpdateExpandCollapseAllButtonLabel();
        }

        private List<object> BuildGroupRows(string headerTitle, Brush accentBrush, List<NoteCardViewModel> items,
            string expanderKey, ContextMenu? headerContextMenu = null, Action? onAddClick = null, string? addTooltip = null)
        {
            bool isExpanded = !_expanderStates.ContainsKey(expanderKey) || _expanderStates[expanderKey];

            var solidAccent = (accentBrush as SolidColorBrush)?.Color ?? Colors.White;
            var pillBackground = new SolidColorBrush(Color.FromArgb(0x28, solidAccent.R, solidAccent.G, solidAccent.B));
            pillBackground.Freeze();

            var header = new NoteGroupHeaderViewModel
            {
                Title = headerTitle,
                AccentBrush = accentBrush,
                PillBackground = pillBackground,
                Count = items.Count,
                ExpanderKey = expanderKey,
                IsExpanded = isExpanded,
                HeaderContextMenu = headerContextMenu,
                OnAddClick = onAddClick,
                AddTooltip = addTooltip ?? "Add new note"
            };

            var rows = new List<object> { header };
            if (isExpanded) rows.AddRange(items);
            return rows;
        }

        private void GroupHeader_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not NoteGroupHeaderViewModel vm) return;
            bool currentlyExpanded = !_expanderStates.ContainsKey(vm.ExpanderKey) || _expanderStates[vm.ExpanderKey];
            _expanderStates[vm.ExpanderKey] = !currentlyExpanded;
            RefreshNotesList();
        }

        private void GroupHeaderAdd_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not NoteGroupHeaderViewModel vm) return;
            vm.OnAddClick?.Invoke();
        }
        private static string BuildCardSnippet(string fullPlainText)
        {
            if (string.IsNullOrEmpty(fullPlainText)) return "";

            int firstNewline = fullPlainText.IndexOf('\n');
            if (firstNewline < 0) return "";

            string rest = fullPlainText.Substring(firstNewline + 1).TrimStart('\r', '\n', ' ', '\t');

            int nextNewline = rest.IndexOf('\n');
            string firstRemainingLine = nextNewline >= 0 ? rest.Substring(0, nextNewline) : rest;
            return firstRemainingLine.Trim();
        }
        private static List<QuickOpenItem> BuildQuickOpenItems(Note note, List<NoteAttachment>? preloadedAttachments = null)
        {
            var items = new List<QuickOpenItem>();

            var attachments = preloadedAttachments ?? DatabaseHelper.GetNoteAttachments(note.Id);
            foreach (var attachment in attachments)
            {
                items.Add(new QuickOpenItem { Label = attachment.FileName, Target = attachment.FilePath, IsFile = true });
            }

            foreach (var (label, url) in NoteContentHelper.ExtractHyperlinks(note.Content))
            {
                items.Add(new QuickOpenItem { Label = string.IsNullOrWhiteSpace(label) ? url : label, Target = url, IsFile = false });
            }

            return items;
        }
        private void QuickOpenButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement element || element.DataContext is not NoteCardViewModel vm) return;

            var note = DatabaseHelper.GetNote(vm.Id);
            if (note == null) return;
            var items = BuildQuickOpenItems(note, vm.Attachments);
            if (items.Count == 0) return;

            if (items.Count == 1)
            {
                OpenQuickOpenItem(items[0]);
                return;
            }

            var menu = new ContextMenu();
            foreach (var item in items)
            {
                var menuItem = new MenuItem { Header = $"{(item.IsFile ? "📎" : "🔗")} {item.Label}" };
                var captured = item;
                menuItem.Click += (s, args) => OpenQuickOpenItem(captured);
                menu.Items.Add(menuItem);
            }
            menu.PlacementTarget = element;
            menu.IsOpen = true;
        }
        private static void OpenQuickOpenItem(QuickOpenItem item)
        {
            if (item.IsFile)
            {
                if (!File.Exists(item.Target))
                {
                    MessageBox.Show("This file no longer exists on disk.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!SecurityHelper.ConfirmDangerousFileExecution(item.Target))
                {
                    return;
                }
            }
            else
            {
                if (!SecurityHelper.IsSafeWebUri(item.Target))
                {
                    MessageBox.Show("The link destination is not a supported web address.", "Invalid Link", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            try
            {
                Process.Start(new ProcessStartInfo(item.Target) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open: {ex.Message}", "Open Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        public void RefreshTagsFilter()
        {
            UpdateFilterChipStyles();
        }

        private void ClearSearchButton_Click(object sender, RoutedEventArgs e)
        {
            if (SearchTextBox != null)
            {
                SearchTextBox.Text = "";
                SearchTextBox.Focus();
            }
        }

        private void FilterAllButton_Click(object sender, RoutedEventArgs e)
        {
            _selectedTagFilter = null;
            _selectedTags.Clear();
            _filterExactDate = null;
            if (HeatmapFilterPill != null) HeatmapFilterPill.Visibility = Visibility.Collapsed;
            _filterColor = null;
            _filterFavoritesOnly = false;
            _filterHasTasksOnly = false;
            _filterHasFilesOnly = false;
            _showOnlyStale = false;
            if (SearchTextBox != null) SearchTextBox.Text = "";
            UpdateFilterChipStyles();
            RefreshNotesList();
        }

        private void FilterFavoritesButton_Click(object sender, RoutedEventArgs e)
        {
            _filterFavoritesOnly = !_filterFavoritesOnly;
            UpdateFilterChipStyles();
            RefreshNotesList();
        }

        private void FilterTasksButton_Click(object sender, RoutedEventArgs e)
        {
            _filterHasTasksOnly = !_filterHasTasksOnly;
            UpdateFilterChipStyles();
            RefreshNotesList();
        }

        private void FilterFilesButton_Click(object sender, RoutedEventArgs e)
        {
            _filterHasFilesOnly = !_filterHasFilesOnly;
            UpdateFilterChipStyles();
            RefreshNotesList();
        }

        private void FilterColorButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            var allItem = new MenuItem
            {
                Header = "All Colors",
                IsChecked = string.IsNullOrEmpty(_filterColor)
            };
            allItem.Click += (s, args) =>
            {
                _filterColor = null;
                UpdateFilterChipStyles();
                RefreshNotesList();
            };
            menu.Items.Add(allItem);
            menu.Items.Add(new Separator());

            var colors = new[]
            {
                ("Yellow", "yellow", "#D49A13"),
                ("Green", "green", "#1A8F54"),
                ("Pink", "pink", "#C2185B"),
                ("Purple", "purple", "#7B1FA2"),
                ("Blue", "blue", "#0288D1"),
                ("Charcoal", "charcoal", "#616161")
            };

            var converter = new BrushConverter();
            foreach (var (name, key, hex) in colors)
            {
                var item = new MenuItem
                {
                    Header = name,
                    IsChecked = string.Equals(_filterColor, key, StringComparison.OrdinalIgnoreCase)
                };
                item.Icon = new System.Windows.Shapes.Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = (Brush)converter.ConvertFromString(hex)!,
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                string colorKey = key;
                item.Click += (s, args) =>
                {
                    _filterColor = colorKey;
                    UpdateFilterChipStyles();
                    RefreshNotesList();
                };
                menu.Items.Add(item);
            }

            menu.PlacementTarget = FilterColorButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        private void FilterTagsButton_Click(object sender, RoutedEventArgs e)
        {
            if (TagMatrixDrawer == null) return;
            bool isVisible = TagMatrixDrawer.Visibility == Visibility.Visible;
            TagMatrixDrawer.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            if (!isVisible)
            {
                RenderTagMatrixPills();
            }
        }

        private void UpdateFilterChipStyles()
        {
            if (FilterAllButton == null) return;

            var activeBrush = new SolidColorBrush(Color.FromRgb(0, 132, 255));
            var inactiveBrush = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255));
            var activeFg = Brushes.White;
            var inactiveFg = new SolidColorBrush(Color.FromRgb(170, 170, 170));

            bool anyFilter = _filterFavoritesOnly || _filterHasTasksOnly || _filterHasFilesOnly ||
                             !string.IsNullOrEmpty(_filterColor) || !string.IsNullOrEmpty(_selectedTagFilter) ||
                             _selectedTags.Count > 0 || _filterExactDate.HasValue || _showOnlyStale;

            FilterAllButton.Background = !anyFilter ? activeBrush : inactiveBrush;
            FilterAllButton.Foreground = !anyFilter ? activeFg : inactiveFg;

            if (FilterFavoritesButton != null)
            {
                FilterFavoritesButton.Background = _filterFavoritesOnly ? activeBrush : inactiveBrush;
                FilterFavoritesButton.Foreground = _filterFavoritesOnly ? activeFg : inactiveFg;
            }

            if (FilterTasksButton != null)
            {
                FilterTasksButton.Background = _filterHasTasksOnly ? activeBrush : inactiveBrush;
                FilterTasksButton.Foreground = _filterHasTasksOnly ? activeFg : inactiveFg;
            }

            if (FilterFilesButton != null)
            {
                FilterFilesButton.Background = _filterHasFilesOnly ? activeBrush : inactiveBrush;
                FilterFilesButton.Foreground = _filterHasFilesOnly ? activeFg : inactiveFg;
            }

            if (FilterColorButton != null)
            {
                FilterColorButton.Background = !string.IsNullOrEmpty(_filterColor) ? activeBrush : inactiveBrush;
                FilterColorButton.Foreground = !string.IsNullOrEmpty(_filterColor) ? activeFg : inactiveFg;
                FilterColorButton.Content = string.IsNullOrEmpty(_filterColor) ? "🎨 Color ▾" : $"🎨 {_filterColor} ▾";
            }

            if (FilterTagsButton != null)
            {
                bool hasTags = _selectedTags.Count > 0 || !string.IsNullOrEmpty(_selectedTagFilter);
                FilterTagsButton.Background = hasTags ? activeBrush : inactiveBrush;
                FilterTagsButton.Foreground = hasTags ? activeFg : inactiveFg;
                if (_selectedTags.Count > 0)
                    FilterTagsButton.Content = $"🏷️ Tags ({_selectedTags.Count}) ▾";
                else if (!string.IsNullOrEmpty(_selectedTagFilter))
                    FilterTagsButton.Content = $"🏷️ #{_selectedTagFilter} ▾";
                else
                    FilterTagsButton.Content = "🏷️ Tags ▾";
            }
        }

        private void GroupHeader_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("StickyNoteCard") && sender is FrameworkElement fe && fe.DataContext is NoteGroupHeaderViewModel vm)
            {
                if (!vm.ExpanderKey.StartsWith("__"))
                {
                    e.Effects = DragDropEffects.Move;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void GroupHeader_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent("StickyNoteCard") && sender is FrameworkElement fe && fe.DataContext is NoteGroupHeaderViewModel vm)
            {
                if (!vm.ExpanderKey.StartsWith("__"))
                {
                    if (e.Data.GetData("StickyNoteCard") is NoteCardViewModel noteVm)
                    {
                        string targetCategory = vm.ExpanderKey;
                        var note = DatabaseHelper.GetNote(noteVm.Id);
                        if (note != null && !string.Equals(note.Category, targetCategory, StringComparison.OrdinalIgnoreCase))
                        {
                            note.Category = targetCategory;
                            DatabaseHelper.UpdateNote(note);
                            if (_openNoteWindows.TryGetValue(note.Id, out var openWnd))
                            {
                                openWnd.UpdateCategory(targetCategory);
                            }
                            ShowStatusToast($"Moved note to \"{targetCategory}\"");
                            RefreshNotesList();
                        }
                    }
                    e.Handled = true;
                }
            }
        }
        private Point _dragStartPoint;
        private void NoteCard_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is Border border && border.DataContext is NoteCardViewModel vm)
            {
                OpenNoteWindow(vm.Id);
                e.Handled = true;
                return;
            }

            _dragStartPoint = e.GetPosition(null);
        }
        private void NoteCard_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed && sender is Border border && border.DataContext is NoteCardViewModel vm)
            {
                Point currentPosition = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DataObject dragData = new DataObject("StickyNoteCard", vm);
                    DragDropEffects result = DragDrop.DoDragDrop(border, dragData, DragDropEffects.Move);

                    if (Win32Helper.GetCursorPos(out Win32Helper.POINT pt))
                    {
                        var sidebarLeft = this.Left;
                        var sidebarRight = this.Left + this.Width;
                        var sidebarTop = this.Top;
                        var sidebarBottom = this.Top + this.Height;

                        if (pt.X < sidebarLeft || pt.X > sidebarRight || pt.Y < sidebarTop || pt.Y > sidebarBottom)
                        {
                            var note = DatabaseHelper.GetNote(vm.Id);
                            if (note != null)
                            {
                                note.X = pt.X - 150;
                                note.Y = pt.Y - 160;
                                note.W = 300;
                                note.H = 320;
                                DatabaseHelper.UpdateNote(note);
                            }

                            OpenNoteWindow(vm.Id);

                            if (_openNoteWindows.TryGetValue(vm.Id, out var openWnd))
                            {
                                openWnd.Left = pt.X - 150;
                                openWnd.Top = pt.Y - 160;
                                openWnd.Width = 300;
                                openWnd.Height = 320;
                            }
                        }
                    }
                }
            }
        }
        private void QuickAddTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not int id) return;

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
                        RefreshNotesList();
                        RefreshTagsFilter();
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
                    RefreshNotesList();
                    RefreshTagsFilter();
                }
            };
            menu.Items.Add(newTagItem);

            menu.PlacementTarget = btn;
            menu.IsOpen = true;
        }
        private void TagBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not Border border) return;
            string? tag = border.DataContext as string;
            if (string.IsNullOrEmpty(tag)) return;

            var parent = VisualTreeHelper.GetParent(border);
            while (parent != null && parent is not Border { Name: "CardBorder" })
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is Border cardBorder && cardBorder.DataContext is NoteCardViewModel noteVm)
            {
                int noteId = noteVm.Id;
                var menu = new ContextMenu();

                var removeItem = new MenuItem { Header = $"Remove #{tag}" };
                removeItem.Click += (s, args) =>
                {
                    DatabaseHelper.RemoveTagFromNote(noteId, tag);
                    RefreshNotesList();
                    RefreshTagsFilter();
                };
                menu.Items.Add(removeItem);

                var renameItem = new MenuItem { Header = "Rename Tag..." };
                renameItem.Click += (s, args) =>
                {
                    var dlg = new InputDialog("Enter new tag name:", "Rename Tag", tag) { Owner = this };
                    if (dlg.ShowDialog() == true && !string.IsNullOrEmpty(dlg.Answer))
                    {
                        string newTag = dlg.Answer.Trim().ToLower();
                        DatabaseHelper.RemoveTagFromNote(noteId, tag);
                        DatabaseHelper.AddTagToNote(noteId, newTag);
                        RefreshNotesList();
                        RefreshTagsFilter();
                    }
                };
                menu.Items.Add(renameItem);

                border.ContextMenu = menu;
            }
        }
        private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string? tag = btn.Tag as string;
            if (string.IsNullOrEmpty(tag)) return;

            var parent = VisualTreeHelper.GetParent(btn);
            while (parent != null && parent is not Border { Name: "CardBorder" })
            {
                parent = VisualTreeHelper.GetParent(parent);
            }

            if (parent is Border cardBorder && cardBorder.DataContext is NoteCardViewModel noteVm)
            {
                DatabaseHelper.RemoveTagFromNote(noteVm.Id, tag);
                RefreshNotesList();
                RefreshTagsFilter();
            }
        }
        private void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var note = DatabaseHelper.GetNote(id);
                if (note != null)
                {
                    DatabaseHelper.SetFavorite(id, !note.IsFavorite);
                    RefreshNotesList();
                }
            }
        }
        private void DeleteCardButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                var ans = MessageBox.Show("Are you sure you want to delete this note?", "Delete Note", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (ans == MessageBoxResult.Yes)
                {
                    var note = DatabaseHelper.GetNote(id);
                    if (note != null && !string.IsNullOrEmpty(note.ImagePath) && File.Exists(note.ImagePath))
                    {
                        try { File.Delete(note.ImagePath); } catch {}
                    }

                    DatabaseHelper.DeleteNote(id);

                    if (_openNoteWindows.TryGetValue(id, out NoteWindow? noteWindow))
                    {
                        noteWindow.Close();
                    }

                    RefreshNotesList();
                    RefreshTagsFilter();
                }
            }
        }
        private void SortCategoryButton_Click(object sender, RoutedEventArgs e)
        {
            _sortOrder = "category";
            SortCategoryButton.Background = new SolidColorBrush(Color.FromRgb(0, 132, 255));
            SortCategoryButton.Foreground = Brushes.White;

            SortDateButton.Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            SortDateButton.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));

            ExpandCollapseAllButton.Visibility = Visibility.Visible;
            UpdateExpandCollapseAllButtonLabel();

            RefreshNotesList();
        }
        private void SortDateButton_Click(object sender, RoutedEventArgs e)
        {
            _sortOrder = "date";
            SortDateButton.Background = new SolidColorBrush(Color.FromRgb(0, 132, 255));
            SortDateButton.Foreground = Brushes.White;

            SortCategoryButton.Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            SortCategoryButton.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));

            ExpandCollapseAllButton.Visibility = Visibility.Visible;
            UpdateExpandCollapseAllButtonLabel();

            RefreshNotesList();
        }
        private List<string> GetAllGroupKeys()
        {
            if (_sortOrder == "date")
            {
                return new List<string>
                {
                    "__favorites__",
                    "__timeline_today__",
                    "__timeline_yesterday__",
                    "__timeline_week__",
                    "__timeline_month__",
                    "__timeline_older__"
                };
            }
            else
            {
                var categories = DatabaseHelper.ListAllCategories();
                return new List<string> { "__favorites__" }.Concat(categories).ToList();
            }
        }

        private void UpdateExpandCollapseAllButtonLabel()
        {
            var keys = GetAllGroupKeys();
            bool anyCollapsed = keys.Any(key => _expanderStates.TryGetValue(key, out var expanded) && !expanded);
            ExpandCollapseAllButton.Content = anyCollapsed ? "Expand All" : "Collapse All";
        }
        private void ExpandCollapseAllButton_Click(object sender, RoutedEventArgs e)
        {
            var groupKeys = GetAllGroupKeys();
            bool anyCollapsed = groupKeys.Any(key => _expanderStates.TryGetValue(key, out var expanded) && !expanded);

            foreach (var key in groupKeys)
            {
                _expanderStates[key] = anyCollapsed;
            }

            RefreshNotesList();
            UpdateExpandCollapseAllButtonLabel();
        }
        private void SizeListButton_Click(object sender, RoutedEventArgs e) => SetCardSize("List");
        private void SizeTableButton_Click(object sender, RoutedEventArgs e) => SetCardSize("Table");
        private void SizeSmallButton_Click(object sender, RoutedEventArgs e) => SetCardSize("Small");
        private void SizeMediumButton_Click(object sender, RoutedEventArgs e) => SetCardSize("Medium");
        private void SizeLargeButton_Click(object sender, RoutedEventArgs e) => SetCardSize("Large");
        private void ViewTasksButton_Click(object sender, RoutedEventArgs e) => SetCardSize("Tasks");
        private void SetCardSize(string size)
        {
            _cardSize = size;
            try
            {
                var config = SettingsService.Current;
                config.CardSize = size;
                SettingsService.Save(config);
            }
            catch { }

            var active = new SolidColorBrush(Color.FromRgb(0, 132, 255));
            var inactive = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            var activeFg = Brushes.White;
            var inactiveFg = new SolidColorBrush(Color.FromRgb(136, 136, 136));

            if (SizeListButton != null)
            {
                SizeListButton.Background = size == "List" ? active : inactive;
                SizeListButton.Foreground = size == "List" ? activeFg : inactiveFg;
            }
            if (SizeTableButton != null)
            {
                SizeTableButton.Background = size == "Table" ? active : inactive;
                SizeTableButton.Foreground = size == "Table" ? activeFg : inactiveFg;
            }
            if (SizeSmallButton != null)
            {
                SizeSmallButton.Background = size == "Small" ? active : inactive;
                SizeSmallButton.Foreground = size == "Small" ? activeFg : inactiveFg;
            }
            if (SizeMediumButton != null)
            {
                SizeMediumButton.Background = size == "Medium" ? active : inactive;
                SizeMediumButton.Foreground = size == "Medium" ? activeFg : inactiveFg;
            }
            if (SizeLargeButton != null)
            {
                SizeLargeButton.Background = size == "Large" ? active : inactive;
                SizeLargeButton.Foreground = size == "Large" ? activeFg : inactiveFg;
            }
            if (ViewTasksButton != null)
            {
                ViewTasksButton.Background = size == "Tasks" ? active : inactive;
                ViewTasksButton.Foreground = size == "Tasks" ? activeFg : inactiveFg;
            }

            RefreshNotesList();
        }
        private void ColorPaletteFromCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int noteId)
            {
                var menu = new ContextMenu();

                var colors = new[] { ("Yellow", "yellow"), ("Green", "green"), ("Pink", "pink"), ("Purple", "purple"), ("Blue", "blue"), ("Charcoal", "charcoal") };
                foreach (var (name, key) in colors)
                {
                    var item = new MenuItem { Header = name, Tag = noteId, CommandParameter = key };
                    item.Click += ChangeColorFromCard_Click;
                    menu.Items.Add(item);
                }

                menu.PlacementTarget = btn;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                menu.IsOpen = true;
            }
        }

        private void CardBorder_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            if (sender is not Border border || border.DataContext is not NoteCardViewModel noteVm) return;

            int noteId = noteVm.Id;
            var menu = new ContextMenu();

            var openItem = new MenuItem { Header = "Open", Tag = noteId };
            openItem.Click += OpenNoteFromCard_Click;
            menu.Items.Add(openItem);

            var favoriteItem = new MenuItem { Header = noteVm.IsFavorite ? "Remove from Favorites" : "Add to Favorites" };
            favoriteItem.Click += (s, args) =>
            {
                var note = DatabaseHelper.GetNote(noteId);
                if (note != null)
                {
                    DatabaseHelper.SetFavorite(noteId, !note.IsFavorite);
                    RefreshNotesList();
                }
            };
            menu.Items.Add(favoriteItem);

            menu.Items.Add(new Separator());

            var colors = new[] { ("Yellow", "yellow"), ("Green", "green"), ("Pink", "pink"), ("Purple", "purple"), ("Blue", "blue"), ("Charcoal", "charcoal") };
            foreach (var (name, key) in colors)
            {
                var item = new MenuItem { Header = $"🎨 {name}", Tag = noteId, CommandParameter = key };
                item.Click += ChangeColorFromCard_Click;
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "Delete", Tag = noteId };
            deleteItem.Click += DeleteNoteFromCard_Click;
            menu.Items.Add(deleteItem);

            border.ContextMenu = menu;
        }
        private void OpenNoteFromCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is int noteId)
            {
                OpenNoteWindow(noteId);
            }
        }
        private void DeleteNoteFromCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is int noteId)
            {
                var res = MessageBox.Show("Are you sure you want to delete this note?", "Delete Note", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (res == MessageBoxResult.Yes)
                {
                    if (_openNoteWindows.TryGetValue(noteId, out var openWnd))
                    {
                        openWnd.Close();
                    }

                    var note = DatabaseHelper.GetNote(noteId);
                    if (note != null && !string.IsNullOrEmpty(note.ImagePath) && File.Exists(note.ImagePath))
                    {
                        try { File.Delete(note.ImagePath); } catch {}
                    }

                    DatabaseHelper.DeleteNote(noteId);
                    RefreshNotesList();
                    RefreshTagsFilter();
                }
            }
        }
        private void ChangeColorFromCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is int noteId && item.CommandParameter is string colorKey)
            {
                var note = DatabaseHelper.GetNote(noteId);
                if (note != null)
                {
                    note.Color = colorKey;
                    DatabaseHelper.UpdateNote(note);

                    if (_openNoteWindows.TryGetValue(noteId, out var openWnd))
                    {
                        openWnd.ChangeColor(colorKey);
                    }

                    RefreshNotesList();
                }
            }
        }
        private void CategoryFromCard_Click(object sender, RoutedEventArgs e)
        {
            if (!(sender is Button btn) || !(btn.Tag is int noteId)) return;

            var note = DatabaseHelper.GetNote(noteId);
            if (note == null) return;

            string currentCategory = note.Category ?? "General";

            var menu = new ContextMenu();

            var existingCategories = DatabaseHelper.ListAllCategories();

            foreach (var cat in existingCategories)
            {
                var menuItem = new MenuItem
                {
                    Header = cat,
                    IsCheckable = true,
                    IsChecked = cat == currentCategory
                };
                string catCopy = cat;
                menuItem.Click += (s, args) =>
                {
                    var n = DatabaseHelper.GetNote(noteId);
                    if (n != null)
                    {
                        n.Category = catCopy;
                        DatabaseHelper.UpdateNote(n);
                        if (_openNoteWindows.TryGetValue(noteId, out var wnd))
                            wnd.UpdateCategory(catCopy);
                        RefreshNotesList();
                    }
                };
                menu.Items.Add(menuItem);
            }

            menu.Items.Add(new Separator());

            var newCatItem = new MenuItem { Header = "+ New Category..." };
            newCatItem.Click += (s, args) =>
            {
                var dialog = new InputDialog("Enter a new category name:", "New Category", currentCategory)
                {
                    Owner = this
                };
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Answer))
                {
                    var n = DatabaseHelper.GetNote(noteId);
                    if (n != null)
                    {
                        n.Category = dialog.Answer.Trim();
                        DatabaseHelper.UpdateNote(n);
                        if (_openNoteWindows.TryGetValue(noteId, out var wnd))
                            wnd.UpdateCategory(n.Category);
                        RefreshNotesList();
                    }
                }
            };
            menu.Items.Add(newCatItem);

            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }

        public void RecordRecentNote(int noteId)
        {
            try
            {
                if (_recentNoteIds == null) _recentNoteIds = new List<int>();
                _recentNoteIds.Remove(noteId);
                _recentNoteIds.Insert(0, noteId);
                if (_recentNoteIds.Count > 8)
                {
                    _recentNoteIds = _recentNoteIds.Take(8).ToList();
                }
                var config = SettingsService.Current;
                config.RecentNoteIds = _recentNoteIds;
                SettingsService.Save(config);

                Dispatcher.InvokeAsync(RenderRecentNotes);
            }
            catch {}
        }

        private void ClearRecentNotes_Click(object sender, RoutedEventArgs e)
        {
            _recentNoteIds.Clear();
            var config = SettingsService.Current;
            config.RecentNoteIds = _recentNoteIds;
            SettingsService.Save(config);
            RenderRecentNotes();
        }

        public void RenderRecentNotes()
        {
            if (RecentNotesBar == null || RecentNotesStackPanel == null) return;
            if (_recentNoteIds == null || _recentNoteIds.Count == 0)
            {
                RecentNotesBar.Visibility = Visibility.Collapsed;
                return;
            }

            RecentNotesStackPanel.Children.Clear();
            int renderedCount = 0;

            foreach (int id in _recentNoteIds.ToList())
            {
                var note = DatabaseHelper.GetNote(id);
                if (note == null) continue;

                renderedCount++;
                string title = string.IsNullOrWhiteSpace(note.Title) ? "Sticky Note" : note.Title;
                if (title.Length > 16) title = title.Substring(0, 15) + "…";

                var btn = new Button
                {
                    Content = title,
                    ToolTip = string.IsNullOrWhiteSpace(note.Title) ? "Sticky Note" : note.Title,
                    Tag = id,
                    Padding = new Thickness(6, 2, 6, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x18, 0xff, 0xff, 0xff)),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xcc, 0xff, 0xff, 0xff)),
                    FontSize = 9.5,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                var style = new Style(typeof(Border));
                style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(4)));
                btn.Resources.Add(typeof(Border), style);

                int capturedId = id;
                btn.Click += (s, ev) => OpenNoteWindow(capturedId);

                var menu = new ContextMenu();
                var removeMenuItem = new MenuItem { Header = "Remove from Recents" };
                removeMenuItem.Click += (s, ev) =>
                {
                    _recentNoteIds.Remove(capturedId);
                    SettingsService.Current.RecentNoteIds = _recentNoteIds;
                    SettingsService.Save(SettingsService.Current);
                    RenderRecentNotes();
                };
                menu.Items.Add(removeMenuItem);
                btn.ContextMenu = menu;

                RecentNotesStackPanel.Children.Add(btn);
            }

            RecentNotesBar.Visibility = renderedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void HeatmapToggleButton_Click(object sender, RoutedEventArgs e)
        {
            if (HeatmapContainer == null) return;
            bool isVisible = HeatmapContainer.Visibility == Visibility.Visible;
            HeatmapContainer.Visibility = isVisible ? Visibility.Collapsed : Visibility.Visible;
            SettingsService.Current.ShowHeatmap = !isVisible;
            SettingsService.Save(SettingsService.Current);

            if (!isVisible)
            {
                RenderActivityHeatmap();
            }
        }

        private void CloseHeatmap_Click(object sender, RoutedEventArgs e)
        {
            if (HeatmapContainer != null)
            {
                HeatmapContainer.Visibility = Visibility.Collapsed;
                SettingsService.Current.ShowHeatmap = false;
                SettingsService.Save(SettingsService.Current);
            }
        }

        private void ClearHeatmapDateFilter_Click(object sender, RoutedEventArgs e)
        {
            _filterExactDate = null;
            if (HeatmapFilterPill != null) HeatmapFilterPill.Visibility = Visibility.Collapsed;
            RefreshNotesList();
        }

        public void RenderActivityHeatmap()
        {
            if (HeatmapGrid == null) return;
            HeatmapGrid.Children.Clear();
            HeatmapGrid.ColumnDefinitions.Clear();
            HeatmapGrid.RowDefinitions.Clear();

            const int weeks = 10;
            const int daysPerWeek = 7;

            var today = DateTime.Today;
            int daysSinceMonday = ((int)today.DayOfWeek - 1 + 7) % 7;
            var currentMonday = today.AddDays(-daysSinceMonday);
            var startDate = currentMonday.AddDays(-7 * (weeks - 1));
            var endDate = today;

            var activityMap = DatabaseHelper.GetDailyActivityHeatmap(startDate, endDate.AddDays(7));
            int totalEdits = activityMap.Values.Sum();

            if (HeatmapStatsText != null)
            {
                HeatmapStatsText.Text = $"{totalEdits} edits in last 10 weeks";
            }

            for (int c = 0; c < weeks; c++)
            {
                HeatmapGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            }
            for (int r = 0; r < daysPerWeek; r++)
            {
                HeatmapGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int w = 0; w < weeks; w++)
            {
                for (int d = 0; d < daysPerWeek; d++)
                {
                    var cellDate = startDate.AddDays(w * 7 + d);
                    if (cellDate > today)
                    {
                        var futureCell = new Border
                        {
                            Width = 11,
                            Height = 11,
                            CornerRadius = new CornerRadius(2),
                            Margin = new Thickness(1.5),
                            Background = new SolidColorBrush(Color.FromArgb(0x06, 0xff, 0xff, 0xff))
                        };
                        Grid.SetColumn(futureCell, w);
                        Grid.SetRow(futureCell, d);
                        HeatmapGrid.Children.Add(futureCell);
                        continue;
                    }

                    string key = cellDate.ToString("yyyy-MM-dd");
                    int count = activityMap.TryGetValue(key, out int cVal) ? cVal : 0;

                    Color cellColor;
                    if (count == 0) cellColor = Color.FromArgb(0x14, 0xff, 0xff, 0xff);
                    else if (count == 1) cellColor = Color.FromRgb(0x1a, 0x6e, 0x3d);
                    else if (count <= 3) cellColor = Color.FromRgb(0x22, 0x99, 0x54);
                    else if (count <= 6) cellColor = Color.FromRgb(0x2e, 0xcc, 0x71);
                    else cellColor = Color.FromRgb(0x58, 0xd6, 0x8d);

                    var cell = new Border
                    {
                        Width = 11,
                        Height = 11,
                        CornerRadius = new CornerRadius(2),
                        Margin = new Thickness(1.5),
                        Background = new SolidColorBrush(cellColor),
                        Cursor = Cursors.Hand,
                        ToolTip = $"{cellDate:ddd, MMM d, yyyy}: {count} edit{(count == 1 ? "" : "s")}"
                    };

                    if (_filterExactDate.HasValue && _filterExactDate.Value.Date == cellDate.Date)
                    {
                        cell.BorderBrush = Brushes.White;
                        cell.BorderThickness = new Thickness(1.5);
                    }

                    var capturedDate = cellDate;
                    cell.MouseLeftButtonUp += (s, ev) =>
                    {
                        if (_filterExactDate.HasValue && _filterExactDate.Value.Date == capturedDate.Date)
                        {
                            _filterExactDate = null;
                            if (HeatmapFilterPill != null) HeatmapFilterPill.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            _filterExactDate = capturedDate;
                            if (HeatmapFilterPillText != null) HeatmapFilterPillText.Text = $"Filtering: {capturedDate:MMM d, yyyy}";
                            if (HeatmapFilterPill != null) HeatmapFilterPill.Visibility = Visibility.Visible;
                        }
                        RenderActivityHeatmap();
                        RefreshNotesList();
                    };

                    Grid.SetColumn(cell, w);
                    Grid.SetRow(cell, d);
                    HeatmapGrid.Children.Add(cell);
                }
            }
        }

        private void SavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialog("Enter a name for this smart preset view:", "Save Preset View", "Standup") { Owner = this };
            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Answer))
            {
                string name = dialog.Answer.Trim();
                var config = SettingsService.Current;
                if (config.SavedViews == null) config.SavedViews = new List<SavedViewPreset>();

                var preset = new SavedViewPreset
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name,
                    SearchText = SearchTextBox?.Text ?? "",
                    Tag = _selectedTagFilter,
                    Color = _filterColor,
                    FavoritesOnly = _filterFavoritesOnly,
                    TasksOnly = _filterHasTasksOnly,
                    FilesOnly = _filterHasFilesOnly,
                    SortOrder = _sortOrder,
                    CardSize = _cardSize
                };

                config.SavedViews.Add(preset);
                SettingsService.Save(config);
                RenderSavedPresets();
            }
        }

        public void RenderSavedPresets()
        {
            if (SavedPresetsContainer == null || SavedPresetsStackPanel == null) return;
            var presets = SettingsService.Current.SavedViews;
            if (presets == null || presets.Count == 0)
            {
                SavedPresetsContainer.Visibility = Visibility.Collapsed;
                return;
            }

            SavedPresetsContainer.Visibility = Visibility.Visible;
            SavedPresetsStackPanel.Children.Clear();

            foreach (var preset in presets)
            {
                var btn = new Button
                {
                    Content = $"⚡ {preset.Name}",
                    ToolTip = $"Apply '{preset.Name}' view (Right-click to delete)",
                    Padding = new Thickness(7, 2, 7, 2),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x20, 0x00, 0x84, 0xff)),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xee, 0xff, 0xff, 0xff)),
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                var style = new Style(typeof(Border));
                style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(3)));
                btn.Resources.Add(typeof(Border), style);

                var capturedPreset = preset;
                btn.Click += (s, ev) => ApplySavedPreset(capturedPreset);

                var menu = new ContextMenu();
                var deleteItem = new MenuItem { Header = $"Delete '{preset.Name}'" };
                deleteItem.Click += (s, ev) =>
                {
                    SettingsService.Current.SavedViews.Remove(capturedPreset);
                    SettingsService.Save(SettingsService.Current);
                    RenderSavedPresets();
                };
                menu.Items.Add(deleteItem);
                btn.ContextMenu = menu;

                SavedPresetsStackPanel.Children.Add(btn);
            }
        }

        private void ApplySavedPreset(SavedViewPreset preset)
        {
            if (SearchTextBox != null) SearchTextBox.Text = preset.SearchText;
            _selectedTagFilter = preset.Tag;
            _filterColor = preset.Color;
            _filterFavoritesOnly = preset.FavoritesOnly;
            _filterHasTasksOnly = preset.TasksOnly;
            _filterHasFilesOnly = preset.FilesOnly;
            _sortOrder = string.IsNullOrEmpty(preset.SortOrder) ? "date" : preset.SortOrder;
            if (!string.IsNullOrEmpty(preset.CardSize))
            {
                SetCardSize(preset.CardSize);
            }

            UpdateFilterChipStyles();
            RefreshNotesList();
        }

        public void RenderCategoryJumpRail()
        {
            if (CategoryJumpRailPanel == null) return;
            CategoryJumpRailPanel.Children.Clear();

            var categories = DatabaseHelper.ListAllCategories();
            if (categories.Count <= 1)
            {
                if (CategoryJumpRailContainer != null)
                    CategoryJumpRailContainer.Visibility = Visibility.Collapsed;
                return;
            }

            if (CategoryJumpRailContainer != null)
                CategoryJumpRailContainer.Visibility = Visibility.Visible;

            foreach (var cat in categories)
            {
                var catBrush = GetCategoryColorBrush(cat);
                var btn = new Button
                {
                    Tag = cat,
                    ToolTip = $"Filter notes in '{cat}'",
                    Padding = new Thickness(6, 1.5, 6, 1.5),
                    Margin = new Thickness(0, 0, 4, 0),
                    Background = new SolidColorBrush(Color.FromArgb(0x14, 0xff, 0xff, 0xff)),
                    Foreground = new SolidColorBrush(Color.FromArgb(0xaa, 0xff, 0xff, 0xff)),
                    FontSize = 8.5,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };
                var style = new Style(typeof(Border));
                style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(3)));
                btn.Resources.Add(typeof(Border), style);

                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var dot = new Ellipse
                {
                    Width = 6,
                    Height = 6,
                    Fill = catBrush,
                    Margin = new Thickness(0, 0, 4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                var tb = new TextBlock
                {
                    Text = cat,
                    VerticalAlignment = VerticalAlignment.Center
                };
                sp.Children.Add(dot);
                sp.Children.Add(tb);
                btn.Content = sp;

                string capturedCat = cat;
                btn.Click += (s, ev) =>
                {
                    if (SearchTextBox == null) return;
                    string target = $"cat:{capturedCat}";
                    if (SearchTextBox.Text.Trim().Equals(target, StringComparison.OrdinalIgnoreCase))
                    {
                        SearchTextBox.Text = "";
                    }
                    else
                    {
                        SearchTextBox.Text = target;
                    }
                    RefreshNotesList();
                };

                CategoryJumpRailPanel.Children.Add(btn);
            }
        }

        private void CloseTagMatrix_Click(object sender, RoutedEventArgs e)
        {
            if (TagMatrixDrawer != null) TagMatrixDrawer.Visibility = Visibility.Collapsed;
        }

        private void TagMatrixModeButton_Click(object sender, RoutedEventArgs e)
        {
            _tagMatrixModeAll = !_tagMatrixModeAll;
            if (TagMatrixModeButton != null)
            {
                TagMatrixModeButton.Content = _tagMatrixModeAll ? "Mode: ALL (AND)" : "Mode: ANY (OR)";
                TagMatrixModeButton.Background = _tagMatrixModeAll ? new SolidColorBrush(Color.FromRgb(0x7b, 0x1f, 0xa2)) : new SolidColorBrush(Color.FromArgb(0x18, 0xff, 0xff, 0xff));
            }
            RefreshNotesList();
        }

        private void TagMatrixSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            RenderTagMatrixPills();
        }

        private void ClearAllTagsFilter_Click(object sender, RoutedEventArgs e)
        {
            _selectedTags.Clear();
            _selectedTagFilter = null;
            RenderTagMatrixPills();
            UpdateFilterChipStyles();
            RefreshNotesList();
        }

        private void ApplyTagMatrix_Click(object sender, RoutedEventArgs e)
        {
            if (TagMatrixDrawer != null) TagMatrixDrawer.Visibility = Visibility.Collapsed;
            RefreshNotesList();
        }

        public void RenderTagMatrixPills()
        {
            if (TagMatrixWrapPanel == null) return;
            TagMatrixWrapPanel.Children.Clear();

            var tagsMap = DatabaseHelper.GetAllNoteTagsMap();
            var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in tagsMap.Values)
            {
                foreach (var t in list)
                {
                    tagCounts[t] = tagCounts.TryGetValue(t, out int count) ? count + 1 : 1;
                }
            }

            string filter = TagMatrixSearchBox?.Text?.Trim() ?? "";
            var sortedTags = tagCounts
                .Where(kvp => string.IsNullOrEmpty(filter) || kvp.Key.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(kvp => kvp.Value)
                .ThenBy(kvp => kvp.Key)
                .ToList();

            if (sortedTags.Count == 0)
            {
                TagMatrixWrapPanel.Children.Add(new TextBlock
                {
                    Text = "No tags found",
                    Foreground = new SolidColorBrush(Color.FromArgb(0x66, 0xff, 0xff, 0xff)),
                    FontSize = 9,
                    Margin = new Thickness(4)
                });
                return;
            }

            foreach (var item in sortedTags)
            {
                bool isSelected = _selectedTags.Contains(item.Key);
                var btn = new Button
                {
                    Content = $"#{item.Key} ({item.Value})",
                    Margin = new Thickness(0, 0, 4, 4),
                    Padding = new Thickness(6, 2, 6, 2),
                    FontSize = 8.5,
                    Cursor = Cursors.Hand,
                    BorderThickness = new Thickness(1),
                    Background = isSelected ? new SolidColorBrush(Color.FromRgb(0x00, 0x84, 0xff)) : new SolidColorBrush(Color.FromArgb(0x18, 0xff, 0xff, 0xff)),
                    Foreground = isSelected ? Brushes.White : new SolidColorBrush(Color.FromArgb(0xcc, 0xff, 0xff, 0xff)),
                    BorderBrush = isSelected ? Brushes.White : new SolidColorBrush(Color.FromArgb(0x22, 0xff, 0xff, 0xff))
                };
                var style = new Style(typeof(Border));
                style.Setters.Add(new Setter(Border.CornerRadiusProperty, new CornerRadius(3)));
                btn.Resources.Add(typeof(Border), style);

                string tagKey = item.Key;
                btn.Click += (s, ev) =>
                {
                    if (_selectedTags.Contains(tagKey))
                        _selectedTags.Remove(tagKey);
                    else
                        _selectedTags.Add(tagKey);

                    RenderTagMatrixPills();
                    UpdateFilterChipStyles();
                    RefreshNotesList();
                };

                TagMatrixWrapPanel.Children.Add(btn);
            }
        }

        private void GlobalTaskCheckBox_Click(object sender, RoutedEventArgs e)
        {
            if (sender is CheckBox cb && cb.Tag is GlobalTaskItemViewModel task)
            {
                bool newState = cb.IsChecked == true;
                task.IsCompleted = newState;
                DatabaseHelper.ToggleTaskInNote(task.NoteId, task.LineIndex, newState);

                if (_openNoteWindows.TryGetValue(task.NoteId, out var openWnd))
                {
                    openWnd.ReloadFromDatabase();
                }

                RefreshNotesList();
            }
        }

        private void GlobalTaskNoteLink_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.Tag is int noteId)
            {
                OpenNoteWindow(noteId);
            }
        }

        private static List<GlobalTaskItemViewModel> ExtractTasksFromNote(NoteCardViewModel noteVm)
        {
            var list = new List<GlobalTaskItemViewModel>();
            if (noteVm.IsSecure || string.IsNullOrEmpty(noteVm.FullPlainText)) return list;

            var lines = noteVm.FullPlainText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                if ((trimmed.StartsWith("- [ ]") || trimmed.StartsWith("* [ ]") ||
                     trimmed.StartsWith("- [x]", StringComparison.OrdinalIgnoreCase) ||
                     trimmed.StartsWith("* [x]", StringComparison.OrdinalIgnoreCase)) && trimmed.Length >= 5)
                {
                    bool isChecked = trimmed.Substring(3, 1).Equals("x", StringComparison.OrdinalIgnoreCase);
                    string taskDesc = trimmed.Substring(5).Trim();
                    if (string.IsNullOrWhiteSpace(taskDesc)) taskDesc = "(Untitled task)";

                    list.Add(new GlobalTaskItemViewModel
                    {
                        NoteId = noteVm.Id,
                        NoteTitle = string.IsNullOrEmpty(noteVm.Title) ? "Sticky Note" : noteVm.Title,
                        NoteBrush = noteVm.CardHeaderBrush,
                        LineIndex = i,
                        TaskText = taskDesc,
                        IsCompleted = isChecked
                    });
                }
            }
            return list;
        }

        private List<object> BuildTaskGroupRows(string headerTitle, Brush accentBrush, List<GlobalTaskItemViewModel> items, string expanderKey)
        {
            bool isExpanded = !_expanderStates.ContainsKey(expanderKey) || _expanderStates[expanderKey];

            var solidAccent = (accentBrush as SolidColorBrush)?.Color ?? Colors.White;
            var pillBackground = new SolidColorBrush(Color.FromArgb(0x28, solidAccent.R, solidAccent.G, solidAccent.B));
            pillBackground.Freeze();

            var header = new NoteGroupHeaderViewModel
            {
                Title = headerTitle,
                AccentBrush = accentBrush,
                PillBackground = pillBackground,
                Count = items.Count,
                ExpanderKey = expanderKey,
                IsExpanded = isExpanded
            };

            var rows = new List<object> { header };
            if (isExpanded) rows.AddRange(items);
            return rows;
        }
    }

    public class GlobalTaskItemViewModel
    {
        public int NoteId { get; set; }
        public string NoteTitle { get; set; } = "";
        public Brush NoteBrush { get; set; } = Brushes.Gray;
        public int LineIndex { get; set; }
        public string TaskText { get; set; } = "";
        public bool IsCompleted { get; set; }
        public Brush TextColor => IsCompleted ? NoteCardViewModel.FrozenBrush(0x88, 0xff, 0xff, 0xff) : Brushes.White;
        public double TextOpacity => IsCompleted ? 0.6 : 1.0;
        public TextDecorationCollection? Strikethrough => IsCompleted ? TextDecorations.Strikethrough : null;
    }

    public class QuickOpenItem
    {
        public string Label { get; set; } = "";
        public string Target { get; set; } = "";
        public bool IsFile { get; set; }
    }

    public class NoteGroupHeaderViewModel
    {
        public string Title { get; set; } = "";
        public Brush AccentBrush { get; set; } = Brushes.White;
        public Brush PillBackground { get; set; } = Brushes.Transparent;
        public int Count { get; set; }
        public string ExpanderKey { get; set; } = "";
        public bool IsExpanded { get; set; }
        public ContextMenu? HeaderContextMenu { get; set; }
        public Action? OnAddClick { get; set; }
        public string AddTooltip { get; set; } = "Add new note";
        public Visibility AddButtonVisibility => OnAddClick != null ? Visibility.Visible : Visibility.Collapsed;
        public string ChevronGlyph => IsExpanded ? "▾" : "▸";
    }

    public class NotesRowTemplateSelector : DataTemplateSelector
    {
        public override DataTemplate? SelectTemplate(object item, DependencyObject container)
        {
            var element = container as FrameworkElement;
            if (item is NoteGroupHeaderViewModel)
                return element?.FindResource("NoteGroupHeaderTemplate") as DataTemplate;

            if (item is GlobalTaskItemViewModel)
                return element?.FindResource("GlobalTaskRowTemplate") as DataTemplate;

            if (item is NoteCardViewModel vm)
            {
                if (vm.CardSize == "List")
                    return element?.FindResource("NoteListItemTemplate") as DataTemplate;
                if (vm.CardSize == "Table")
                    return element?.FindResource("NoteTableRowTemplate") as DataTemplate;
            }

            return element?.FindResource("NoteCardTemplate") as DataTemplate;
        }
    }

    public class NoteCardViewModel
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Color { get; set; } = "yellow";
        public string Snippet { get; set; } = "";
        public string FullPlainText { get; set; } = "";
        public string? ImagePath { get; set; }
        public List<string> Tags { get; set; } = new List<string>();
        public string Category { get; set; } = "General";
        public bool IsFavorite { get; set; }
        public bool IsSecure { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string CardSize { get; set; } = "Medium";

        public List<NoteAttachment> Attachments { get; set; } = new List<NoteAttachment>();
        public bool HasLikelyLink { get; set; }

        public Visibility SnippetVisibility => CardSize == "Small" ? Visibility.Collapsed : Visibility.Visible;
        public Visibility TagsRowVisibility => CardSize == "Small" ? Visibility.Collapsed : Visibility.Visible;

        public string DisplayTitle { get; private set; } = "";
        public string DateText { get; private set; } = "";
        public string TagsList { get; private set; } = "";
        public Visibility QuickOpenVisibility { get; private set; } = Visibility.Collapsed;
        public string QuickOpenIcon { get; private set; } = "";
        public string QuickOpenToolTip { get; private set; } = "";
        public string? ResolvedImagePath { get; private set; }
        public Visibility ImageVisibility => (CardSize != "Small" && !string.IsNullOrEmpty(ResolvedImagePath)) ? Visibility.Visible : Visibility.Collapsed;

        public int TotalTasks { get; private set; }
        public int CompletedTasks { get; private set; }
        public double TaskProgressPercentage { get; private set; }
        public string TaskStatsText { get; private set; } = "";
        public Visibility TaskProgressVisibility => (CardSize != "Small" && TotalTasks > 0) ? Visibility.Visible : Visibility.Collapsed;

        internal static readonly Brush FavoriteOnBrush = FrozenBrush(0xff, 0xff, 0xc1, 0x07);
        internal static readonly Brush FavoriteOffBrush = FrozenBrush(0x80, 0xff, 0xff, 0xff);
        public static Brush FrozenBrush(byte a, byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
            brush.Freeze();
            return brush;
        }

        public string FavoriteIcon => IsFavorite ? "★" : "☆";
        public Brush FavoriteBrush => IsFavorite ? FavoriteOnBrush : FavoriteOffBrush;
        public string FavoriteToolTip => IsFavorite ? "Unpin favorite" : "Mark as favorite";

        public void InitComputedProperties()
        {
            DisplayTitle = (IsSecure ? "🔒 " : "") + (string.IsNullOrEmpty(Title) ? "Sticky Note" : Title);
            DateText = FormatDate(UpdatedAt);
            TagsList = Tags.Count > 0 ? string.Join("  ", Tags.Select(t => $"#{t}")) : "";

            bool hasAttachments = Attachments.Count > 0;
            QuickOpenVisibility = (hasAttachments || HasLikelyLink) ? Visibility.Visible : Visibility.Collapsed;
            QuickOpenIcon = (hasAttachments ? "📎" : "🔗") + "️";
            if (Attachments.Count == 1 && !HasLikelyLink) QuickOpenToolTip = $"Open {Attachments[0].FileName}";
            else if (Attachments.Count > 1 && !HasLikelyLink) QuickOpenToolTip = $"Open ({Attachments.Count} files)";
            else if (Attachments.Count == 0 && HasLikelyLink) QuickOpenToolTip = "Open link";
            else QuickOpenToolTip = "Open attached files/links";

            if (!IsSecure)
            {
                if (!string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath))
                {
                    ResolvedImagePath = ImagePath;
                }
                else if (hasAttachments)
                {
                    foreach (var att in Attachments)
                    {
                        if (IsImageFile(att.FilePath) && File.Exists(att.FilePath))
                        {
                            ResolvedImagePath = att.FilePath;
                            break;
                        }
                    }
                }
            }

            ParseTaskCounts(FullPlainText, out int total, out int completed);
            TotalTasks = total;
            CompletedTasks = completed;
            TaskProgressPercentage = total > 0 ? ((double)completed / total) * 100 : 0;
            TaskStatsText = $"{completed} of {total} tasks";
        }

        private static string FormatDate(DateTime updatedAt)
        {
            var now = DateTime.Now;
            var span = now - updatedAt;
            if (span.TotalMinutes < 1) return "Just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24 && updatedAt.Date == now.Date) return $"{(int)span.TotalHours}h ago";
            if (updatedAt.Date == now.Date.AddDays(-1)) return "Yesterday";
            if (updatedAt.Year == now.Year) return updatedAt.ToString("MMM d");
            return updatedAt.ToString("MMM d, yyyy");
        }

        private static void ParseTaskCounts(string text, out int total, out int completed)
        {
            total = 0;
            completed = 0;
            if (string.IsNullOrEmpty(text)) return;

            int len = text.Length;
            for (int i = 0; i <= len - 5; i++)
            {
                char c0 = text[i];
                if (c0 == '-' || c0 == '*')
                {
                    if (text[i + 1] == ' ' && text[i + 2] == '[' && (i + 4 < len) && text[i + 4] == ']')
                    {
                        char mark = text[i + 3];
                        if (mark == ' ')
                        {
                            total++;
                            i += 4;
                        }
                        else if (mark == 'x' || mark == 'X')
                        {
                            total++;
                            completed++;
                            i += 4;
                        }
                    }
                }
            }
        }

        private static bool IsImageFile(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            string ext = System.IO.Path.GetExtension(path);
            return ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
        }

        private static readonly Dictionary<string, BitmapImage> ThumbnailCache = new Dictionary<string, BitmapImage>();

        public BitmapImage? ThumbnailSource
        {
            get
            {
                if (string.IsNullOrEmpty(ResolvedImagePath)) return null;

                string path = ResolvedImagePath;
                string cacheKey;
                try { cacheKey = $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}"; }
                catch { cacheKey = path; }

                if (ThumbnailCache.TryGetValue(cacheKey, out var cached)) return cached;

                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 100;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    ThumbnailCache[cacheKey] = bitmap;
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }
        }

        private static readonly Dictionary<string, (string bg, string border, string text)> ColorsConfig =
            new Dictionary<string, (string bg, string border, string text)>
        {
            { "yellow", ("#3C221C12", "#D49A13", "#ffffff") },
            { "green", ("#3C122018", "#1A8F54", "#ffffff") },
            { "pink", ("#3C221218", "#C2185B", "#ffffff") },
            { "purple", ("#3C1B1220", "#7B1FA2", "#ffffff") },
            { "blue", ("#3C121C22", "#0288D1", "#ffffff") },
            { "charcoal", ("#3C1B1B1B", "#424242", "#ffffff") }
        };

        private static readonly Dictionary<string, (Brush bg, Brush border, Brush text)> BrushCache = BuildBrushCache();
        private static Dictionary<string, (Brush bg, Brush border, Brush text)> BuildBrushCache()
        {
            var converter = new System.Windows.Media.BrushConverter();
            var result = new Dictionary<string, (Brush, Brush, Brush)>();
            foreach (var kvp in ColorsConfig)
            {
                Brush bg = (Brush)converter.ConvertFromString(kvp.Value.bg)!; bg.Freeze();
                Brush border = (Brush)converter.ConvertFromString(kvp.Value.border)!; border.Freeze();
                Brush text = (Brush)converter.ConvertFromString(kvp.Value.text)!; text.Freeze();
                result[kvp.Key] = (bg, border, text);
            }
            return result;
        }
        private (Brush bg, Brush border, Brush text) CardBrushes => BrushCache.TryGetValue(Color, out var c) ? c : BrushCache["yellow"];

        public Brush CardBackground => CardBrushes.bg;
        public Brush CardHeaderBrush => CardBrushes.border;
        public Brush CardTextBrush => CardBrushes.text;
    }
}
