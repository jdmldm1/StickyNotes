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
        private string _sortOrder = "date";
        private static readonly string[] CategoryColors = { "#D49A13", "#1A8F54", "#C2185B", "#7B1FA2", "#0288D1", "#e65100" };
        private static Brush GetCategoryColorBrush(string categoryName)
        {
            if (string.IsNullOrEmpty(categoryName) || categoryName == "General")
                return Brushes.White;

            string? customHex = DatabaseHelper.GetCategoryColor(categoryName);
            var converter = new System.Windows.Media.BrushConverter();
            if (!string.IsNullOrEmpty(customHex))
            {
                try
                {
                    return (Brush)converter.ConvertFromString(customHex)!;
                }
                catch {}
            }

            int hash = 0;
            foreach (char c in categoryName)
                hash = hash * 31 + c;

            int idx = Math.Abs(hash) % CategoryColors.Length;
            return (Brush)converter.ConvertFromString(CategoryColors[idx])!;
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
                onChanged();
            };
            menu.Items.Add(resetItem);

            return menu;
        }
        private static readonly Dictionary<string, bool> _expanderStates = new Dictionary<string, bool>();
        public void RefreshNotesList()
        {
            if (NotesGroupPanel == null) return;
            NotesGroupPanel.Children.Clear();

            string searchQuery = SearchTextBox.Text.Trim();
            var notes = DatabaseHelper.ListNotes(
                string.IsNullOrEmpty(searchQuery) ? null : searchQuery,
                _selectedTagFilter
            );

            if (_showOnlyStale)
            {
                notes = notes.Where(IsStaleNote).ToList();
            }

            var sortedNotes = notes.OrderByDescending(n => n.IsFavorite).ThenByDescending(n => n.UpdatedAt).ToList();

            var tagsMap = DatabaseHelper.GetAllNoteTagsMap();
            var attachmentsMap = DatabaseHelper.GetAllNoteAttachmentsMap();

            var viewModels = sortedNotes.Select(n =>
            {
                string fullText = GetPlainTextFromXaml(n.Content);
                List<string> tags = tagsMap.TryGetValue(n.Id, out var t) ? t : new List<string>();
                List<NoteAttachment> attachments = attachmentsMap.TryGetValue(n.Id, out var a) ? a : new List<NoteAttachment>();
                return new NoteCardViewModel
                {
                    Id = n.Id,
                    Title = n.Title,
                    Color = n.Color,
                    Snippet = BuildCardSnippet(fullText),
                    FullPlainText = fullText,
                    ImagePath = n.ImagePath,
                    Tags = tags,
                    Category = n.Category ?? "General",
                    IsFavorite = n.IsFavorite,
                    UpdatedAt = n.UpdatedAt,
                    QuickOpenItems = BuildQuickOpenItems(n, attachments)
                };
            }).ToList();

            var template = (DataTemplate)this.FindResource("NoteCardTemplate");

            if (_sortOrder == "category")
            {
                var favorites = viewModels.Where(vm => vm.IsFavorite).ToList();
                if (favorites.Count > 0)
                {
                    var favoritesHeader = new TextBlock
                    {
                        Text = $"★ Favorites ({favorites.Count})",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xc1, 0x07)),
                        FontWeight = FontWeights.Bold,
                        FontSize = 12.5,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    NotesGroupPanel.Children.Add(favoritesHeader);

                    var favoritesItemsControl = new ItemsControl
                    {
                        ItemTemplate = template,
                        ItemsSource = favorites,
                        Margin = new Thickness(0, 0, 0, 12)
                    };
                    NotesGroupPanel.Children.Add(favoritesItemsControl);
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
                    var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };
                    var headerText = new TextBlock
                    {
                        Text = $"{categoryName} ({groupItems.Count})",
                        Foreground = catBrush,
                        FontWeight = FontWeights.Bold,
                        FontSize = 12.5,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    headerPanel.Children.Add(headerText);

                    var addBtn = new Button
                    {
                        Content = "➕",
                        ToolTip = $"Add new note to {categoryName}",
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0),
                        Foreground = catBrush,
                        FontSize = 10,
                        Cursor = Cursors.Hand,
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    string currentCatName = categoryName;
                    addBtn.PreviewMouseLeftButtonDown += (s, args) =>
                    {
                        args.Handled = true;
                        CreateNewNote(currentCatName);
                    };
                    headerPanel.Children.Add(addBtn);

                    headerPanel.ContextMenu = CreateCategoryContextMenu(categoryName, () =>
                    {
                        RefreshNotesList();
                        _noteManagerWnd?.RefreshCategoryTabs();
                    });

                    var expander = new Expander
                    {
                        Header = headerPanel,
                        Foreground = Brushes.White,
                        FontWeight = FontWeights.Bold,
                        FontSize = 12.5,
                        Margin = new Thickness(0, 0, 0, 12),
                        Background = Brushes.Transparent,
                        BorderThickness = new Thickness(0)
                    };

                    expander.IsExpanded = !_expanderStates.ContainsKey(categoryName) || _expanderStates[categoryName];
                    expander.Expanded += (s, e) => _expanderStates[categoryName] = true;
                    expander.Collapsed += (s, e) => _expanderStates[categoryName] = false;

                    var itemsControl = new ItemsControl
                    {
                        ItemTemplate = template,
                        ItemsSource = groupItems,
                        Margin = new Thickness(6, 8, 0, 0)
                    };

                    expander.Content = itemsControl;
                    NotesGroupPanel.Children.Add(expander);
                }
            }
            else
            {
                var favorites = viewModels.Where(vm => vm.IsFavorite).ToList();
                var rest = viewModels.Where(vm => !vm.IsFavorite).ToList();

                if (favorites.Count > 0)
                {
                    var favoritesHeader = new TextBlock
                    {
                        Text = $"★ Favorites ({favorites.Count})",
                        Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0xc1, 0x07)),
                        FontWeight = FontWeights.Bold,
                        FontSize = 12.5,
                        Margin = new Thickness(0, 0, 0, 8)
                    };
                    NotesGroupPanel.Children.Add(favoritesHeader);

                    var favoritesItemsControl = new ItemsControl
                    {
                        ItemTemplate = template,
                        ItemsSource = favorites,
                        Margin = new Thickness(0, 0, 0, 12)
                    };
                    NotesGroupPanel.Children.Add(favoritesItemsControl);
                }

                const string notesExpanderKey = "__notes__";
                var notesExpander = new Expander
                {
                    Header = $"Notes ({rest.Count})",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.Bold,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 0, 0, 12),
                    Background = Brushes.Transparent,
                    BorderThickness = new Thickness(0)
                };
                notesExpander.IsExpanded = !_expanderStates.ContainsKey(notesExpanderKey) || _expanderStates[notesExpanderKey];
                notesExpander.Expanded += (s, e) => _expanderStates[notesExpanderKey] = true;
                notesExpander.Collapsed += (s, e) => _expanderStates[notesExpanderKey] = false;

                var restItemsControl = new ItemsControl
                {
                    ItemTemplate = template,
                    ItemsSource = rest,
                    Margin = new Thickness(6, 8, 0, 0)
                };
                notesExpander.Content = restItemsControl;
                NotesGroupPanel.Children.Add(notesExpander);
            }
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
            if (vm.QuickOpenItems.Count == 0) return;

            if (vm.QuickOpenItems.Count == 1)
            {
                OpenQuickOpenItem(vm.QuickOpenItems[0]);
                return;
            }

            var menu = new ContextMenu();
            foreach (var item in vm.QuickOpenItems)
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
            if (item.IsFile && !File.Exists(item.Target))
            {
                MessageBox.Show("This file no longer exists on disk.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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
            TagsFilterPanel.Children.Clear();

            TagsFilterPanel.Children.Add(BuildFilterPill("All", string.IsNullOrEmpty(_selectedTagFilter), () =>
            {
                _selectedTagFilter = null;
                _showOnlyStale = false;
                RefreshTagsFilter();
                RefreshNotesList();
            }));

            var tags = DatabaseHelper.ListAllTags();
            foreach (var tag in tags)
            {
                string currentTag = tag;
                TagsFilterPanel.Children.Add(BuildFilterPill($"#{tag}", _selectedTagFilter == tag, () =>
                {
                    _selectedTagFilter = currentTag;
                    _showOnlyStale = false;
                    RefreshTagsFilter();
                    RefreshNotesList();
                }));
            }
        }
        private static Border BuildFilterPill(string label, bool isSelected, Action onClick)
        {
            var border = new Border
            {
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(12, 4, 12, 4),
                CornerRadius = new CornerRadius(11),
                Background = isSelected ? new SolidColorBrush(Color.FromRgb(0, 132, 255)) : new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                Cursor = Cursors.Hand
            };
            border.Child = new TextBlock { Text = label, Foreground = Brushes.White, FontSize = 11 };
            border.MouseLeftButtonUp += (s, e) => onClick();
            return border;
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

            RefreshNotesList();
        }
        private void SortDateButton_Click(object sender, RoutedEventArgs e)
        {
            _sortOrder = "date";
            SortDateButton.Background = new SolidColorBrush(Color.FromRgb(0, 132, 255));
            SortDateButton.Foreground = Brushes.White;
            
            SortCategoryButton.Background = new SolidColorBrush(Color.FromArgb(32, 255, 255, 255));
            SortCategoryButton.Foreground = new SolidColorBrush(Color.FromRgb(136, 136, 136));

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

            var existingCategories = DatabaseHelper.ListNotes(null, null)
                .Select(n => n.Category ?? "General")
                .Distinct()
                .OrderBy(c => c)
                .ToList();

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
    }

    public class QuickOpenItem
    {
        public string Label { get; set; } = "";
        public string Target { get; set; } = "";
        public bool IsFile { get; set; }
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
        public DateTime UpdatedAt { get; set; }
        public List<QuickOpenItem> QuickOpenItems { get; set; } = new List<QuickOpenItem>();

        public string DateText
        {
            get
            {
                var span = DateTime.Now - UpdatedAt;
                if (span.TotalMinutes < 1) return "Just now";
                if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
                if (span.TotalHours < 24 && UpdatedAt.Date == DateTime.Now.Date) return $"{(int)span.TotalHours}h ago";
                if (UpdatedAt.Date == DateTime.Now.Date.AddDays(-1)) return "Yesterday";
                if (UpdatedAt.Year == DateTime.Now.Year) return UpdatedAt.ToString("MMM d");
                return UpdatedAt.ToString("MMM d, yyyy");
            }
        }

        public string FavoriteIcon => IsFavorite ? "★" : "☆";
        public Brush FavoriteBrush => IsFavorite ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xff, 0xc1, 0x07)) : new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x80, 0xff, 0xff, 0xff));
        public string FavoriteToolTip => IsFavorite ? "Unpin favorite" : "Mark as favorite";

        public string DisplayTitle => string.IsNullOrEmpty(Title) ? "Sticky Note" : Title;

        public Visibility QuickOpenVisibility => QuickOpenItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        public string QuickOpenIcon => (QuickOpenItems.Any(i => i.IsFile) ? "📎" : "🔗") + "️";

        public string QuickOpenToolTip => QuickOpenItems.Count == 1
            ? $"Open {QuickOpenItems[0].Label}"
            : $"Open ({QuickOpenItems.Count} links/files)";

        public string TagsList => Tags.Count > 0 ? string.Join("  ", Tags.Select(t => $"#{t}")) : "";

        public Visibility ImageVisibility
        {
            get
            {
                if (!string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath))
                {
                    return Visibility.Visible;
                }
                var attachments = DatabaseHelper.GetNoteAttachments(Id);
                if (attachments != null)
                {
                    foreach (var att in attachments)
                    {
                        if (IsImageFile(att.FilePath) && File.Exists(att.FilePath))
                        {
                            return Visibility.Visible;
                        }
                    }
                }
                return Visibility.Collapsed;
            }
        }

        private bool IsImageFile(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLower();
            return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".gif" || ext == ".bmp";
        }

        public int TotalTasks => CountOccurrences(FullPlainText, "- [ ]") + CountOccurrences(FullPlainText, "- [x]") + CountOccurrences(FullPlainText, "* [ ]") + CountOccurrences(FullPlainText, "* [x]");
        public int CompletedTasks => CountOccurrences(FullPlainText, "- [x]") + CountOccurrences(FullPlainText, "* [x]");

        public double TaskProgressPercentage => TotalTasks > 0 ? ((double)CompletedTasks / TotalTasks) * 100 : 0;
        public string TaskStatsText => $"{CompletedTasks} of {TotalTasks} tasks";
        public Visibility TaskProgressVisibility => TotalTasks > 0 ? Visibility.Visible : Visibility.Collapsed;

        private int CountOccurrences(string text, string pattern)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(pattern)) return 0;
            int count = 0;
            int index = 0;
            while ((index = text.IndexOf(pattern, index, StringComparison.OrdinalIgnoreCase)) != -1)
            {
                count++;
                index += pattern.Length;
            }
            return count;
        }

        public BitmapImage? ThumbnailSource
        {
            get
            {
                string? path = null;
                if (!string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath))
                {
                    path = ImagePath;
                }
                else
                {
                    var attachments = DatabaseHelper.GetNoteAttachments(Id);
                    if (attachments != null)
                    {
                        foreach (var att in attachments)
                        {
                            if (IsImageFile(att.FilePath) && File.Exists(att.FilePath))
                            {
                                path = att.FilePath;
                                break;
                            }
                        }
                    }
                }

                if (path == null) return null;
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 100;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
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

        public System.Windows.Media.Brush CardBackground
        {
            get
            {
                string key = Color;
                if (!ColorsConfig.ContainsKey(key)) key = "yellow";
                return (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(ColorsConfig[key].bg)!;
            }
        }

        public System.Windows.Media.Brush CardHeaderBrush
        {
            get
            {
                string key = Color;
                if (!ColorsConfig.ContainsKey(key)) key = "yellow";
                return (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(ColorsConfig[key].border)!;
            }
        }

        public System.Windows.Media.Brush CardTextBrush
        {
            get
            {
                string key = Color;
                if (!ColorsConfig.ContainsKey(key)) key = "yellow";
                return (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(ColorsConfig[key].text)!;
            }
        }
    }

}
