using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StickyNotes__
{
    public partial class KanbanWindow : Window
    {
        private readonly MainWindow _mainWindow;
        private List<Note> _allNotes = new();
        private string _searchQuery = "";
        private string _selectedCategory = "All";

        private static readonly string[] BacklogTags = { "backlog", "todo", "idea" };
        private static readonly string[] InProgressTags = { "in-progress", "doing", "active", "wip" };
        private static readonly string[] SuspenseTags = { "suspense", "review", "blocked", "waiting" };
        private static readonly string[] DoneTags = { "done", "completed", "finished" };

        private static readonly Dictionary<string, Color> ColorMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "yellow",   Color.FromRgb(0xEA, 0xB3, 0x08) },
            { "green",    Color.FromRgb(0x22, 0xC5, 0x5E) },
            { "pink",     Color.FromRgb(0xEC, 0x48, 0x99) },
            { "purple",   Color.FromRgb(0xA8, 0x55, 0xF7) },
            { "blue",     Color.FromRgb(0x38, 0xBD, 0xF8) },
            { "charcoal", Color.FromRgb(0x64, 0x74, 0x8B) },
        };

        public KanbanWindow(MainWindow mainWindow)
        {
            InitializeComponent();
            _mainWindow = mainWindow;
            Loaded += (s, e) =>
            {
                PopulateCategories();
                RefreshBoard();
            };
        }

        private void PopulateCategories()
        {
            try
            {
                var cats = new List<string> { "All" };
                cats.AddRange(DatabaseHelper.ListAllCategories());
                CategoryCombo.ItemsSource = cats;
                CategoryCombo.SelectedIndex = 0;
            }
            catch { }
        }

        public void RefreshBoard()
        {
            try
            {
                _allNotes = DatabaseHelper.ListNotes(includeContent: true);
                RenderColumns();
            }
            catch { }
        }

        private void RenderColumns()
        {
            BacklogColumnPanel.Children.Clear();
            InProgressColumnPanel.Children.Clear();
            SuspenseColumnPanel.Children.Clear();
            DoneColumnPanel.Children.Clear();

            int backlogCount = 0, inProgCount = 0, suspenseCount = 0, doneCount = 0;

            var filtered = _allNotes.Where(n =>
            {
                if (_selectedCategory != "All" && !string.Equals(n.Category, _selectedCategory, StringComparison.OrdinalIgnoreCase))
                    return false;

                if (!string.IsNullOrWhiteSpace(_searchQuery))
                {
                    string q = _searchQuery.ToLowerInvariant();
                    bool titleMatch = n.Title.ToLowerInvariant().Contains(q);
                    bool textMatch = (n.PlainText ?? "").ToLowerInvariant().Contains(q);
                    if (!titleMatch && !textMatch) return false;
                }
                return true;
            }).ToList();

            foreach (var note in filtered)
            {
                var tags = DatabaseHelper.GetNoteTags(note.Id);
                string stage = DetermineStage(tags);

                var card = CreateCardElement(note, tags, stage);

                switch (stage)
                {
                    case "done":
                        DoneColumnPanel.Children.Add(card);
                        doneCount++;
                        break;
                    case "suspense":
                        SuspenseColumnPanel.Children.Add(card);
                        suspenseCount++;
                        break;
                    case "in-progress":
                        InProgressColumnPanel.Children.Add(card);
                        inProgCount++;
                        break;
                    default:
                        BacklogColumnPanel.Children.Add(card);
                        backlogCount++;
                        break;
                }
            }

            BacklogCountText.Text = backlogCount.ToString();
            InProgressCountText.Text = inProgCount.ToString();
            SuspenseCountText.Text = suspenseCount.ToString();
            DoneCountText.Text = doneCount.ToString();

            int total = backlogCount + inProgCount + suspenseCount + doneCount;
            TotalCardCountText.Text = $"{total} total note card{(total == 1 ? "" : "s")}";
        }

        private static string DetermineStage(List<string> tags)
        {
            if (tags.Any(t => DoneTags.Contains(t, StringComparer.OrdinalIgnoreCase))) return "done";
            if (tags.Any(t => SuspenseTags.Contains(t, StringComparer.OrdinalIgnoreCase))) return "suspense";
            if (tags.Any(t => InProgressTags.Contains(t, StringComparer.OrdinalIgnoreCase))) return "in-progress";
            return "backlog";
        }

        private UIElement CreateCardElement(Note note, List<string> tags, string currentStage)
        {
            Color accentColor = ColorMap.TryGetValue(note.Color, out Color c) ? c : ColorMap["yellow"];

            var card = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x28)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(50, accentColor.R, accentColor.G, accentColor.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand,
                Tag = note.Id
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var headerGrid = new Grid();
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var dot = new Border
            {
                Width = 8, Height = 8, CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(accentColor),
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(dot, 0);
            headerGrid.Children.Add(dot);

            string displayTitle = string.IsNullOrWhiteSpace(note.Title) ? "Untitled Note" : note.Title;
            if (note.IsSecure) displayTitle += " 🔒";

            var titleBlock = new TextBlock
            {
                Text = displayTitle,
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 1);
            headerGrid.Children.Add(titleBlock);

            var shiftStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            if (currentStage != "backlog")
            {
                var prevBtn = new Button
                {
                    Content = "◀", ToolTip = "Move to previous column",
                    Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xA0)),
                    BorderThickness = new Thickness(0), FontSize = 9, Padding = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand
                };
                int nid = note.Id;
                string prevStage = GetPreviousStage(currentStage);
                prevBtn.Click += (s, e) => MoveNoteToStage(nid, prevStage);
                shiftStack.Children.Add(prevBtn);
            }

            if (currentStage != "done")
            {
                var nextBtn = new Button
                {
                    Content = "▶", ToolTip = "Move to next column",
                    Background = Brushes.Transparent, Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xA0)),
                    BorderThickness = new Thickness(0), FontSize = 9, Padding = new Thickness(3, 0, 3, 0), Cursor = Cursors.Hand
                };
                int nid = note.Id;
                string nextStage = GetNextStage(currentStage);
                nextBtn.Click += (s, e) => MoveNoteToStage(nid, nextStage);
                shiftStack.Children.Add(nextBtn);
            }

            Grid.SetColumn(shiftStack, 2);
            headerGrid.Children.Add(shiftStack);

            Grid.SetRow(headerGrid, 0);
            grid.Children.Add(headerGrid);

            string snippet = note.PlainText ?? "";
            if (snippet.Length > 90) snippet = snippet.Substring(0, 87) + "…";
            if (!string.IsNullOrWhiteSpace(snippet))
            {
                var snippetBlock = new TextBlock
                {
                    Text = snippet,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0x94, 0xA8)),
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 4)
                };
                Grid.SetRow(snippetBlock, 1);
                grid.Children.Add(snippetBlock);
            }

            var tagsWrap = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
            if (!string.IsNullOrEmpty(note.Category) && note.Category != "General")
            {
                tagsWrap.Children.Add(CreateTagBadge("📁 " + note.Category, Color.FromRgb(0x2A, 0x3F, 0x5F), Color.FromRgb(0x90, 0xCD, 0xF4)));
            }

            foreach (var t in tags.Take(3))
            {
                if (IsStatusTag(t)) continue;
                tagsWrap.Children.Add(CreateTagBadge("#" + t, Color.FromRgb(0x26, 0x26, 0x36), Color.FromRgb(0xD0, 0xD0, 0xE0)));
            }

            Grid.SetRow(tagsWrap, 2);
            grid.Children.Add(tagsWrap);

            card.Child = grid;

            int capturedId = note.Id;
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ClickCount == 2)
                {
                    _mainWindow.OpenNoteWindow(capturedId);
                    e.Handled = true;
                }
                else if (e.ButtonState == MouseButtonState.Pressed)
                {
                    DragDrop.DoDragDrop(card, capturedId, DragDropEffects.Move);
                }
            };

            return card;
        }

        private static Border CreateTagBadge(string text, Color bg, Color fg)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(bg),
                CornerRadius = new CornerRadius(3),
                Padding = new Thickness(4, 1, 4, 1),
                Margin = new Thickness(0, 0, 4, 2)
            };
            b.Child = new TextBlock { Text = text, Foreground = new SolidColorBrush(fg), FontSize = 8.5 };
            return b;
        }

        private static bool IsStatusTag(string tag)
        {
            return BacklogTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ||
                   InProgressTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ||
                   SuspenseTags.Contains(tag, StringComparer.OrdinalIgnoreCase) ||
                   DoneTags.Contains(tag, StringComparer.OrdinalIgnoreCase);
        }

        private static string GetNextStage(string current) => current switch
        {
            "backlog" => "in-progress",
            "in-progress" => "suspense",
            "suspense" => "done",
            _ => "done"
        };

        private static string GetPreviousStage(string current) => current switch
        {
            "done" => "suspense",
            "suspense" => "in-progress",
            "in-progress" => "backlog",
            _ => "backlog"
        };

        private void MoveNoteToStage(int noteId, string targetStage)
        {
            try
            {
                var existingTags = DatabaseHelper.GetNoteTags(noteId);

                foreach (var st in existingTags.Where(IsStatusTag))
                {
                    DatabaseHelper.RemoveTagFromNote(noteId, st);
                }

                string newTag = targetStage switch
                {
                    "in-progress" => "in-progress",
                    "suspense" => "suspense",
                    "done" => "done",
                    _ => "backlog"
                };
                DatabaseHelper.AddTagToNote(noteId, newTag);

                _mainWindow.RefreshNotesList();
                RefreshBoard();
            }
            catch { }
        }

        #region Drag and Drop

        private void Column_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(typeof(int)))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void Column_Drop(object sender, DragEventArgs e)
        {
            if (sender is StackPanel panel && panel.Tag is string targetStage)
            {
                if (e.Data.GetData(typeof(int)) is int noteId)
                {
                    MoveNoteToStage(noteId, targetStage);
                    e.Handled = true;
                }
            }
        }

        #endregion

        #region Add Note Helpers

        private void AddBacklog_Click(object sender, RoutedEventArgs e) => CreateAndTagNote("backlog");
        private void AddInProgress_Click(object sender, RoutedEventArgs e) => CreateAndTagNote("in-progress");
        private void AddSuspense_Click(object sender, RoutedEventArgs e) => CreateAndTagNote("suspense");
        private void AddDone_Click(object sender, RoutedEventArgs e) => CreateAndTagNote("done");

        private void CreateAndTagNote(string stageTag)
        {
            string title = stageTag switch
            {
                "in-progress" => "In-Progress Task",
                "suspense" => "Suspense / Review Item",
                "done" => "Completed Item",
                _ => "New Backlog Item"
            };

            int id = DatabaseHelper.CreateNote(title, "", null, null, "yellow");
            try { DatabaseHelper.AddTagToNote(id, stageTag); } catch { }

            _mainWindow.RefreshNotesList();
            RefreshBoard();
            _mainWindow.OpenNoteWindow(id);
        }

        #endregion

        #region Window chrome

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
        private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void Maximize_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshBoard();

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchQuery = SearchBox.Text.Trim();
            RenderColumns();
        }

        private void CategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CategoryCombo.SelectedItem is string cat)
            {
                _selectedCategory = cat;
                RenderColumns();
            }
        }

        #endregion
    }
}
