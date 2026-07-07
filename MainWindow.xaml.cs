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
        private Win32Helper.APPBARDATA _appBarData;
        private bool _isAppBarRegistered;
        private string? _selectedTagFilter;
        private bool _showOnlyStale;
        private bool _staleNotesDismissedThisSession;
        private static readonly TimeSpan StaleNoteAge = TimeSpan.FromDays(3);
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private SpotlightWindow? _spotlightWnd;
        private QuickCaptureWindow? _quickCaptureWnd;
        private NoteManagerWindow? _noteManagerWnd;
        private GraphWindow? _graphWnd;
        private TemplatePickerWindow? _templatePickerWnd;
        private ClipboardPickerWindow? _clipboardPickerWnd;
        private string _sortOrder = "date"; // "date" or "category"



        // Clipboard History fields
        private System.Windows.Threading.DispatcherTimer? _clipboardTimer;
        private readonly List<ClipboardHistoryItem> _clipboardHistory = new List<ClipboardHistoryItem>();
        private int _lastClipboardSequence = -1;

        // Active floating note windows
        private readonly Dictionary<int, NoteWindow> _openNoteWindows = new Dictionary<int, NoteWindow>();

        public MainWindow()
        {
            InitializeComponent();
            DatabaseHelper.InitDatabase();
            InitializeNotifyIcon();
            StartClipboardMonitor();
        }

        private void InitializeNotifyIcon()
        {
            _notifyIcon = new System.Windows.Forms.NotifyIcon();
            try
            {
                var iconUri = new Uri("pack://application:,,,/app_icon.ico");
                var streamInfo = Application.GetResourceStream(iconUri);
                if (streamInfo?.Stream != null)
                    _notifyIcon.Icon = new System.Drawing.Icon(streamInfo.Stream);
                else
                    _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            catch
            {
                _notifyIcon.Icon = System.Drawing.SystemIcons.Application;
            }
            _notifyIcon.Text = "StickyNotes++";
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += (s, e) => RestoreFromTray();

            var contextMenu = new System.Windows.Forms.ContextMenuStrip();
            
            var openItem = new System.Windows.Forms.ToolStripMenuItem("Open StickyNotes++");
            openItem.Click += (s, e) => RestoreFromTray();
            contextMenu.Items.Add(openItem);
            
            var exitItem = new System.Windows.Forms.ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) => ExitApplication();
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
        }

        private void RestoreFromTray()
        {
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;
            this.Left = screenWidth - 350;
            this.Top = 0;
            this.Width = 350;
            this.Height = screenHeight;

            RegisterAppBar();
            this.Show();
            this.WindowState = WindowState.Normal;
            this.Activate();
        }

        private void ExitApplication()
        {
            _notifyIcon?.Dispose();
            _notifyIcon = null;

            var openWindows = new List<NoteWindow>(_openNoteWindows.Values);
            foreach (var noteWnd in openWindows)
            {
                try { noteWnd.Close(); } catch {}
            }

            try { _noteManagerWnd?.Close(); } catch { }
            try { _graphWnd?.Close(); } catch { }

            Application.Current.Shutdown();
        }

        protected override void OnStateChanged(EventArgs e)
        {
            if (this.WindowState == WindowState.Minimized)
            {
                UnregisterAppBar();
                this.Hide();
                this.WindowState = WindowState.Normal;
            }
            base.OnStateChanged(e);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            _notifyIcon?.Dispose();
            base.OnClosing(e);
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            UnregisterAppBar();
            this.Hide();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            ExitApplication();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshNotesList();
            RefreshTagsFilter();
            LoadSavedOpacity();
            CheckStaleNotes();
        }

        // A note "has an open task" if it contains an unchecked checklist item -- see
        // NoteWindow's UncheckedGlyph -- and hasn't been touched in a few days. Surfacing these
        // closes the loop on notes that get filed under a checkbox and then never looked at again.
        private static bool IsStaleNote(Note note)
        {
            if (DateTime.Now - note.UpdatedAt < StaleNoteAge) return false;
            return NoteContentHelper.ExtractPlainText(note.Content).Contains('☐'); // ☐
        }

        private void CheckStaleNotes()
        {
            if (_staleNotesDismissedThisSession)
            {
                StaleNotesBanner.Visibility = Visibility.Collapsed;
                return;
            }

            int staleCount = DatabaseHelper.ListNotes(null, null).Count(IsStaleNote);
            if (staleCount > 0)
            {
                StaleNotesText.Text = $"⏰ {staleCount} note{(staleCount == 1 ? "" : "s")} {(staleCount == 1 ? "has" : "have")} open tasks untouched for {StaleNoteAge.Days}+ days";
                StaleNotesBanner.Visibility = Visibility.Visible;
            }
            else
            {
                StaleNotesBanner.Visibility = Visibility.Collapsed;
            }
        }

        private void StaleNotesBanner_Click(object sender, MouseButtonEventArgs e)
        {
            _showOnlyStale = true;
            RefreshNotesList();
        }

        private void DismissStaleNotesBanner_Click(object sender, RoutedEventArgs e)
        {
            _staleNotesDismissedThisSession = true;
            StaleNotesBanner.Visibility = Visibility.Collapsed;
        }

        public void ApplySidebarOpacity(double opacity)
        {
            opacity = Math.Max(0.2, Math.Min(1.0, opacity));
            byte alpha = (byte)(opacity * 255);
            // #121212 base color
            MainWindowBorder.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromArgb(alpha, 0x12, 0x12, 0x12));
        }

        private void LoadSavedOpacity()
        {
            try
            {
                var config = SettingsService.Current;
                if (config.SidebarOpacity > 0)
                    ApplySidebarOpacity(config.SidebarOpacity);
            }
            catch { }
        }

        private void Window_SourceInitialized(object sender, EventArgs e)
        {
            var wndHelper = new WindowInteropHelper(this);
            HwndSource source = HwndSource.FromHwnd(wndHelper.Handle);
            source.AddHook(WndProcHook);

            _spotlightWnd = new SpotlightWindow(this);
            _quickCaptureWnd = new QuickCaptureWindow(this);

            // Snapping Right (AppBar)
            RegisterAppBar();

            // Register Hotkeys on thread queue
            ComponentDispatcher.ThreadFilterMessage += ComponentDispatcher_ThreadFilterMessage;
            RegisterAllHotKeys();

            // Note: intentionally NOT enabling the Mica backdrop here. Mica composites its own
            // blurred/tinted wallpaper sample underneath the window, which fights with the sidebar
            // opacity slider (ApplySidebarOpacity only controls a plain WPF alpha color) -- lowering
            // opacity produced a muted haze instead of real see-through transparency. Leaving Mica
            // off lets AllowsTransparency blend the Border's alpha directly against the desktop, so
            // the slider actually makes the sidebar transparent as expected.
            Win32Helper.DwmSetWindowAttribute(wndHelper.Handle, Win32Helper.DWMWA_USE_IMMERSIVE_DARK_MODE, ref _darkModeValue, sizeof(int));
        }

        private int _darkModeValue = 1;

        private IntPtr WndProcHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_ACTIVATE = 0x0006;

            if (msg == WM_ACTIVATE)
            {
                if (_isAppBarRegistered)
                {
                    Win32Helper.SHAppBarMessage(Win32Helper.ABM_ACTIVATE, ref _appBarData);
                }
            }

            return IntPtr.Zero;
        }

        private void RegisterAllHotKeys()
        {
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyNewNote, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x4E); // N
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyScreenshot, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x53); // S
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeySpotlight, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x20); // Space
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyBrowserTabs, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x54); // T
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeySaveFiles, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x46); // F
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyMeetingNote, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x4D); // M
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyQuickCapture, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x51); // Q
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyGraph, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x47); // G
            Win32Helper.RegisterHotKey(IntPtr.Zero, Win32Helper.HotkeyToggleSidebar, Win32Helper.MOD_WIN | Win32Helper.MOD_ALT | Win32Helper.MOD_NOREPEAT, 0x5A); // Z
        }

        private void UnregisterAllHotKeys()
        {
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyNewNote);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyScreenshot);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeySpotlight);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyBrowserTabs);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeySaveFiles);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyMeetingNote);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyQuickCapture);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyGraph);
            Win32Helper.UnregisterHotKey(IntPtr.Zero, Win32Helper.HotkeyToggleSidebar);
        }

        private void ComponentDispatcher_ThreadFilterMessage(ref MSG msg, ref bool handled)
        {
            const int WM_HOTKEY = 0x0312;
            if (msg.message == WM_HOTKEY)
            {
                int id = msg.wParam.ToInt32();
                if (id == Win32Helper.HotkeyNewNote)
                {
                    CreateNewNote();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyScreenshot)
                {
                    TakeRegionScreenshot();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeySpotlight)
                {
                    ToggleSpotlight();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyBrowserTabs)
                {
                    SaveBrowserTabs();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeySaveFiles)
                {
                    SaveFilesToNewNote();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyMeetingNote)
                {
                    CreateQuickMeetingNote();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyQuickCapture)
                {
                    ToggleQuickCapture();
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyGraph)
                {
                    GraphButton_Click(this, new RoutedEventArgs());
                    handled = true;
                }
                else if (id == Win32Helper.HotkeyToggleSidebar)
                {
                    ToggleSidebar();
                    handled = true;
                }
            }
        }

        #region AppBar Implementation

        private void RegisterAppBar()
        {
            if (_isAppBarRegistered) return;

            var wndHelper = new WindowInteropHelper(this);
            if (wndHelper.Handle == IntPtr.Zero) return;

            _appBarData = new Win32Helper.APPBARDATA();
            _appBarData.cbSize = Marshal.SizeOf(typeof(Win32Helper.APPBARDATA));
            _appBarData.hWnd = wndHelper.Handle;
            _appBarData.uCallbackMessage = 0x8000 + 101; // WM_USER + 101
            _appBarData.uEdge = Win32Helper.ABE_RIGHT;

            Win32Helper.SHAppBarMessage(Win32Helper.ABM_NEW, ref _appBarData);
            _isAppBarRegistered = true;

            SetAppBarPosition(350);
        }

        private void UnregisterAppBar()
        {
            if (_isAppBarRegistered)
            {
                Win32Helper.SHAppBarMessage(Win32Helper.ABM_REMOVE, ref _appBarData);
                _isAppBarRegistered = false;
            }
        }

        private (double dpiX, double dpiY) GetDpiFactors()
        {
            double dpiX = 1.0;
            double dpiY = 1.0;
            var source = PresentationSource.FromVisual(this);
            if (source != null && source.CompositionTarget != null)
            {
                dpiX = source.CompositionTarget.TransformToDevice.M11;
                dpiY = source.CompositionTarget.TransformToDevice.M22;
            }
            return (dpiX, dpiY);
        }

        private void SetAppBarPosition(int width)
        {
            if (!_isAppBarRegistered) return;

            var wndHelper = new WindowInteropHelper(this);
            var (dpiX, dpiY) = GetDpiFactors();
            
            // Query screen monitor metrics in logical pixels
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            // Convert to physical pixels for Win32 AppBar registration
            int physicalLeft = (int)((screenWidth - width) * dpiX);
            int physicalTop = 0;
            int physicalRight = (int)(screenWidth * dpiX);
            int physicalBottom = (int)(screenHeight * dpiY);

            _appBarData.rc.Left = physicalLeft;
            _appBarData.rc.Top = physicalTop;
            _appBarData.rc.Right = physicalRight;
            _appBarData.rc.Bottom = physicalBottom;

            Win32Helper.SHAppBarMessage(Win32Helper.ABM_QUERYPOS, ref _appBarData);
            
            // Re-assert desired physical bounds to prevent the OS from shifting left on multiple calls
            _appBarData.rc.Left = physicalLeft;
            _appBarData.rc.Top = physicalTop;
            _appBarData.rc.Right = physicalRight;
            _appBarData.rc.Bottom = physicalBottom;

            Win32Helper.SHAppBarMessage(Win32Helper.ABM_SETPOS, ref _appBarData);

            if (width > 0)
            {
                // Convert approved physical bounds back to logical pixels for WPF properties
                this.Left = _appBarData.rc.Left / dpiX;
                this.Top = _appBarData.rc.Top / dpiY;
                this.Width = (_appBarData.rc.Right - _appBarData.rc.Left) / dpiX;
                this.Height = (_appBarData.rc.Bottom - _appBarData.rc.Top) / dpiY;

                Win32Helper.SetWindowPos(
                    wndHelper.Handle,
                    IntPtr.Zero,
                    _appBarData.rc.Left,
                    _appBarData.rc.Top,
                    _appBarData.rc.Right - _appBarData.rc.Left,
                    _appBarData.rc.Bottom - _appBarData.rc.Top,
                    0
                );
            }
        }

        #endregion

        #region Note Commands

        public void CreateNewNote(string category = "General")
        {
            int noteId = DatabaseHelper.CreateNote("", "", null, null, "yellow");
            if (!string.IsNullOrEmpty(category) && category != "General")
            {
                var note = DatabaseHelper.GetNote(noteId);
                if (note != null)
                {
                    note.Category = category;
                    DatabaseHelper.UpdateNote(note);
                }
            }
            RefreshNotesList();
            
            var noteWindow = OpenNoteWindow(noteId);
            noteWindow.FocusTitle();
        }

        private void SpotlightButton_Click(object sender, RoutedEventArgs e)
        {
            ToggleSpotlight();
        }

        private void NoteManagerButton_Click(object sender, RoutedEventArgs e)
        {
            if (_noteManagerWnd == null || !_noteManagerWnd.IsLoaded)
            {
                _noteManagerWnd = new NoteManagerWindow(this);
                _noteManagerWnd.Show();
            }
            else
            {
                _noteManagerWnd.Activate();
                if (_noteManagerWnd.WindowState == WindowState.Minimized)
                    _noteManagerWnd.WindowState = WindowState.Normal;
            }
        }

        private void GraphButton_Click(object sender, RoutedEventArgs e)
        {
            if (_graphWnd == null || !_graphWnd.IsLoaded)
            {
                _graphWnd = new GraphWindow(this);
                _graphWnd.Show();
            }
            else
            {
                _graphWnd.Activate();
                if (_graphWnd.WindowState == WindowState.Minimized)
                    _graphWnd.WindowState = WindowState.Normal;
            }
        }

        public void OpenTemplatePicker()
        {
            if (_templatePickerWnd == null || !_templatePickerWnd.IsLoaded)
            {
                _templatePickerWnd = new TemplatePickerWindow(this);
                _templatePickerWnd.ShowDialog();
            }
            else
            {
                _templatePickerWnd.Activate();
            }
        }

        public void CreateNoteFromUserTemplate(int templateNoteId)
        {
            var templateNote = DatabaseHelper.GetNote(templateNoteId);
            if (templateNote == null) return;

            var dlg = new InputDialog("Enter a title for the new note:", "New from Template", templateNote.Title) { Owner = this };
            if (dlg.ShowDialog() != true) return;
            string title = dlg.Answer.Trim();

            int noteId = DatabaseHelper.CreateNote(title, templateNote.Content, color: templateNote.Color);
            var newNote = DatabaseHelper.GetNote(noteId);
            if (newNote != null)
            {
                newNote.Category = templateNote.Category;
                DatabaseHelper.UpdateNote(newNote);
            }
            RefreshNotesList();
            OpenNoteWindow(noteId);
        }

        public void CreateNoteFromBuiltInTemplate(string templateName)
        {
            var template = NoteTemplates.Find(t => t.Name == templateName);
            if (template != null)
                CreateNoteFromTemplate(template);
            else
                CreateNewNote(); // fallback
        }

        public void OpenGraphWindow()
        {
            GraphButton_Click(this, new RoutedEventArgs());
        }

        public void OpenNoteManager()
        {
            NoteManagerButton_Click(this, new RoutedEventArgs());
        }

        private void ToggleSpotlight()
        {
            if (_spotlightWnd == null) return;

            if (_spotlightWnd.IsVisible)
            {
                _spotlightWnd.Hide();
            }
            else
            {
                _spotlightWnd.Show();
                _spotlightWnd.Activate();
                _spotlightWnd.FocusSearch();
            }
        }

        public void ToggleQuickCapture()
        {
            if (_quickCaptureWnd == null) return;

            if (_quickCaptureWnd.IsVisible)
            {
                _quickCaptureWnd.Hide();
            }
            else
            {
                _quickCaptureWnd.Show();
                _quickCaptureWnd.Activate();
                _quickCaptureWnd.FocusCapture();
            }
        }

        public void ToggleSidebar()
        {
            if (this.Visibility == Visibility.Visible)
            {
                UnregisterAppBar();
                this.Hide();
            }
            else
            {
                RestoreFromTray();
            }
        }

        public NoteWindow OpenNoteWindow(int noteId)
        {
            if (_openNoteWindows.TryGetValue(noteId, out NoteWindow? openWindow))
            {
                openWindow.Activate();
                if (openWindow.WindowState == WindowState.Minimized)
                    openWindow.WindowState = WindowState.Normal;
                return openWindow;
            }

            var noteWindow = new NoteWindow(noteId) { Owner = this };
            noteWindow.Show();
            _openNoteWindows.Add(noteId, noteWindow);
            return noteWindow;
        }

        public void NotifyNoteWindowClosed(int noteId)
        {
            _openNoteWindows.Remove(noteId);
            RefreshNotesList();
            RefreshTagsFilter();
        }

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

            // Favorites always float to the top, then newest first within each group
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
                // Favorites always show at the top of category view as well (Request 2)
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

                // Group by category, sorting General category last
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

                    // Track and restore expander state
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
                // Flat chronological list, but favorites get their own always-visible section up
                // top so it's obvious why a just-edited note isn't at the very top of the list --
                // everything else lives in a collapsible "Notes" section below.
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

        // Invoked from the Settings window -- this is a one-time/occasional utility, not
        // something used often enough to justify permanent sidebar real estate.
        public async Task RunAutoOrganizeAsync()
        {
            try
            {
                if (!await AiHelper.IsOllamaRunningAsync())
                {
                    MessageBox.Show("Please start Ollama (local AI service) to use the Auto Organizer.", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var notes = DatabaseHelper.ListNotes(null, null);
                if (notes.Count == 0)
                {
                    MessageBox.Show("No notes found to organize.", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("You are a system organizer. Group these notes into 3 to 5 logical category names (e.g. Work, Personal, Code, Finance). Also, assign 1 to 3 relevant, brief tags to each note.");
                sb.AppendLine("Respond with ONLY a raw JSON object where keys are note IDs (as strings) and values are objects containing 'category' (string) and 'tags' (array of strings).");
                sb.AppendLine("Example format:");
                sb.AppendLine("{\n  \"1\": { \"category\": \"Work\", \"tags\": [\"deadline\", \"invoice\"] }\n}");
                sb.AppendLine("Do not include any explanations, markdown code blocks, backticks, or formatting. Strictly JSON.");
                sb.AppendLine("Notes list:");
                foreach (var note in notes)
                {
                    string plain = GetPlainTextFromXaml(note.Content);
                    if (plain.Length > 80) plain = plain.Substring(0, 80);
                    sb.AppendLine($"ID: {note.Id} | Title: {note.Title} | Snippet: {plain}");
                }

                string response = await AiHelper.GenerateTextAsync(sb.ToString());
                if (string.IsNullOrEmpty(response))
                {
                    MessageBox.Show("AI did not respond. Check if Ollama is active.", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Try to extract JSON from response (handling potential code block wraps)
                int firstBrace = response.IndexOf('{');
                int lastBrace = response.LastIndexOf('}');
                if (firstBrace == -1 || lastBrace == -1)
                {
                    MessageBox.Show("Could not parse AI response: JSON structure not found.", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string json = response.Substring(firstBrace, lastBrace - firstBrace + 1);
                bool wasTruncated = false;
                Dictionary<string, NoteOrganizationResult>? mappings;
                try
                {
                    mappings = JsonSerializer.Deserialize<Dictionary<string, NoteOrganizationResult>>(json);
                }
                catch (JsonException)
                {
                    // Smaller/faster models (e.g. llama3.2:1b) sometimes cut the response off
                    // mid-object once many notes are involved. Try to recover by trimming back
                    // to the last fully-closed note entry and re-closing the object there,
                    // rather than losing the whole batch to one truncated entry.
                    string? repaired = TryRepairTruncatedJsonObject(json);
                    mappings = repaired != null ? TryDeserializeMappings(repaired) : null;
                    wasTruncated = mappings != null;
                }

                if (mappings == null)
                {
                    MessageBox.Show(
                        "The AI's response wasn't valid JSON, so no notes were organized. This is more common with smaller/faster models -- try again, or switch to a larger model in Settings for more reliable results.",
                        "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var pair in mappings)
                {
                    if (int.TryParse(pair.Key, out int noteId))
                    {
                        var note = notes.FirstOrDefault(n => n.Id == noteId);
                        if (note != null)
                        {
                            note.Category = pair.Value.category?.Trim() ?? "General";
                            DatabaseHelper.UpdateNote(note);

                            // Clear existing tags and add new ones
                            DatabaseHelper.ClearNoteTags(noteId);
                            if (pair.Value.tags != null)
                            {
                                foreach (var tag in pair.Value.tags)
                                {
                                    DatabaseHelper.AddTagToNote(noteId, tag);
                                }
                            }
                        }
                    }
                }

                RefreshNotesList();
                RefreshTagsFilter();

                if (wasTruncated || mappings.Count < notes.Count)
                {
                    MessageBox.Show(
                        $"Organized {mappings.Count} of {notes.Count} notes. The AI's response was incomplete (common with smaller/faster models) -- run Auto Organize again to catch the rest.",
                        "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show("Notes successfully organized and tagged!", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Organization error: {ex.Message}", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static Dictionary<string, NoteOrganizationResult>? TryDeserializeMappings(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, NoteOrganizationResult>>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        // Scans a (possibly truncated) top-level JSON object and, if the tail got cut off
        // mid-entry, trims back to the last fully-closed "key": {...} entry and re-closes the
        // object there. Returns null if no complete entry could be found at all.
        private static string? TryRepairTruncatedJsonObject(string json)
        {
            int depth = 0;
            bool inString = false;
            bool escape = false;
            int lastCompleteEntryEnd = -1;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (escape) { escape = false; continue; }
                if (c == '\\' && inString) { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 1) lastCompleteEntryEnd = i;
                }
            }

            if (lastCompleteEntryEnd <= 0) return null;
            return json.Substring(0, lastCompleteEntryEnd + 1) + "}";
        }

        private string GetPlainTextFromXaml(string xaml) => NoteContentHelper.ExtractPlainText(xaml);

        // A note's title is always derived from the first line of its content (see
        // NoteWindow.SaveNoteContent), so showing the full plain text under the title repeats
        // that first line verbatim. Skip it and show only whatever comes after, on one line.
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

        public async void TakeRegionScreenshot()
        {
            // Minimize current main sidebar temporarily to avoid taking it in screenshot
            this.WindowState = WindowState.Minimized;
            
            // Wait 350ms for minimize animation to complete
            await System.Threading.Tasks.Task.Delay(350);

            try
            {
                var captureWnd = new CaptureWindow();
                bool? dialogResult = null;
                try
                {
                    dialogResult = captureWnd.ShowDialog();
                }
                catch (Exception ex)
                {
                    RestoreFromTray();
                    MessageBox.Show($"Capture window error: {ex.Message}", "Screensnip", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (dialogResult == true && !string.IsNullOrEmpty(captureWnd.CapturedImagePath))
                {
                    string imagePath = captureWnd.CapturedImagePath;

                    // Restore sidebar instantly from tray
                    RestoreFromTray();

                    // Wait a tiny bit for the restore transition to begin/complete before opening the note
                    await System.Threading.Tasks.Task.Delay(150);

                    // Create new note with screenshot details instantly (empty OCR text for now)
                    int noteId = DatabaseHelper.CreateNote(
                        "Screenshot note", 
                        "", 
                        imagePath, 
                        "", 
                        "yellow"
                    );

                    // Open note window and refresh notes list instantly
                    var noteWindow = OpenNoteWindow(noteId);
                    RefreshNotesList();
                    RefreshTagsFilter();

                    // Run OCR and AI tagging asynchronously in the background
                    _ = System.Threading.Tasks.Task.Run(async () =>
                    {
                        string ocrText = "";
                        List<string> ocrTags = new List<string>();
                        try
                        {
                            var ocrResult = await OcrHelper.PerformOcrAsync(imagePath);
                            ocrText = ocrResult.Text ?? "";
                            ocrTags = ocrResult.Tags ?? new List<string>();
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"OCR failed (non-fatal): {ex.Message}");
                        }

                        // Add auto tags (regex + Ollama AI if running)
                        var tags = new HashSet<string>(ocrTags);
                        try
                        {
                            if (!string.IsNullOrEmpty(ocrText) && await AiHelper.IsOllamaRunningAsync())
                            {
                                var aiTags = await AiHelper.AutoTagTextAsync(ocrText);
                                foreach (var tag in aiTags)
                                {
                                    tags.Add(tag.Trim().ToLower());
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"AI tagging failed (non-fatal): {ex.Message}");
                        }

                        // Add tags to note in database
                        foreach (var tag in tags)
                        {
                            if (!string.IsNullOrWhiteSpace(tag))
                                DatabaseHelper.AddTagToNote(noteId, tag);
                        }

                        // If OCR text was detected, update note content in database
                        if (!string.IsNullOrEmpty(ocrText))
                        {
                            string xamlContent = "";
                            Dispatcher.Invoke(() =>
                            {
                                var doc = new FlowDocument();
                                doc.Blocks.Add(new Paragraph(new Run(ocrText)));
                                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                                xamlContent = NoteContentHelper.SaveRange(range);
                            });

                            var updatedNote = DatabaseHelper.GetNote(noteId);
                            if (updatedNote != null)
                            {
                                string currentPlainText = NoteContentHelper.ExtractPlainText(updatedNote.Content).Trim();
                                if (string.IsNullOrEmpty(currentPlainText))
                                {
                                    updatedNote.Content = xamlContent;
                                    updatedNote.OcrText = ocrText;
                                    DatabaseHelper.UpdateNote(updatedNote);
                                }
                            }
                        }

                        // Notify UI thread to reload the note content in the open note window
                        Dispatcher.Invoke(() =>
                        {
                            if (noteWindow != null && noteWindow.IsLoaded)
                            {
                                noteWindow.ReloadNoteFromDb();
                            }
                            RefreshNotesList();
                            RefreshTagsFilter();
                        });
                    });
                }
                else
                {
                    // Restore sidebar if cancelled
                    RestoreFromTray();
                }
            }
            catch (Exception ex)
            {
                RestoreFromTray();
                MessageBox.Show($"Screensnip error: {ex.Message}", "Screensnip", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SaveBrowserTabs()
        {
            List<BrowserTab> tabs;
            try
            {
                tabs = BrowserTabHelper.GetOpenTabs();
            }
            catch (Exception ex)
            {
                ShowStatusToast("Couldn't read browser tabs: " + ex.Message);
                return;
            }

            if (tabs.Count == 0)
            {
                ShowStatusToast("No open Chrome or Edge tabs found.");
                return;
            }

            foreach (var tab in tabs)
            {
                string xamlContent = BuildHyperlinkNoteXaml(tab.Title, tab.Url);
                int noteId = DatabaseHelper.CreateNote(tab.Title, xamlContent, null, null, "blue");
                DatabaseHelper.AddTagToNote(noteId, "Browser Tab");
            }

            RefreshNotesList();
            RefreshTagsFilter();
            ShowStatusToast($"Saved {tabs.Count} browser tab{(tabs.Count == 1 ? "" : "s")}");
        }

        private static string BuildHyperlinkNoteXaml(string title, string url)
        {
            var titleParagraph = new Paragraph(new Run(title)) { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };

            var linkParagraph = new Paragraph { Margin = new Thickness(0) };
            var hyperlink = new Hyperlink(new Run(url))
            {
                NavigateUri = new Uri(url),
                Foreground = new SolidColorBrush(Color.FromRgb(0x4d, 0xb8, 0xff))
            };
            linkParagraph.Inlines.Add(hyperlink);

            // See BuildMeetingNoteXaml for why this explicit Foreground/FontFamily is required.
            var document = new FlowDocument(titleParagraph)
            {
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif")
            };
            document.Blocks.Add(linkParagraph);

            var range = new TextRange(document.ContentStart, document.ContentEnd);
            return NoteContentHelper.SaveRange(range);
        }

        private async void SaveFilesToNewNote()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select files to save as a note",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != true) return;

            string[] filePaths = dialog.FileNames;
            if (filePaths.Length == 0) return;

            string title = filePaths.Length == 1
                ? System.IO.Path.GetFileNameWithoutExtension(filePaths[0])
                : $"{filePaths.Length} Files";

            int noteId = DatabaseHelper.CreateNote(title, "", null, null, "yellow");
            int attachedCount = 0;
            foreach (var path in filePaths)
            {
                try
                {
                    await DatabaseHelper.AddAttachmentAsync(noteId, path);
                    attachedCount++;
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Couldn't attach \"{System.IO.Path.GetFileName(path)}\": {ex.Message}", "Attach Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            DatabaseHelper.AddTagToNote(noteId, "Files");

            RefreshNotesList();
            RefreshTagsFilter();
            OpenNoteWindow(noteId);
            ShowStatusToast($"Saved {attachedCount} file{(attachedCount == 1 ? "" : "s")} to a new note");
        }

        private class NoteTemplateDef
        {
            public string Name = "";
            public string Icon = "";
            public string Category = "General";
            public string Color = "yellow";
            public string? Tag;
            public List<(string Heading, bool Bulleted)> Sections = new List<(string, bool)>();
        }

        private static readonly List<NoteTemplateDef> NoteTemplates = new List<NoteTemplateDef>
        {
            new NoteTemplateDef { Name = "Blank Note", Icon = "📄" },
            new NoteTemplateDef { Name = "Meeting Notes", Icon = "🗓️" }, // handled specially -- reuses CreateQuickMeetingNote
            new NoteTemplateDef
            {
                Name = "1:1 Meeting", Icon = "🗣️", Category = "Meetings", Color = "blue", Tag = "1-1",
                Sections = new List<(string, bool)> { ("Agenda:", true), ("Discussion Topics:", true), ("Action Items:", true), ("Follow-up:", true) }
            },
            new NoteTemplateDef
            {
                Name = "Daily Standup", Icon = "☀️", Category = "Meetings", Color = "green", Tag = "standup",
                Sections = new List<(string, bool)> { ("Yesterday:", true), ("Today:", true), ("Blockers:", true) }
            },
            new NoteTemplateDef
            {
                Name = "Bug Report", Icon = "🐛", Category = "Work", Color = "pink", Tag = "bug",
                Sections = new List<(string, bool)> { ("Summary:", false), ("Steps to Reproduce:", true), ("Expected Behavior:", false), ("Actual Behavior:", false), ("Environment:", false) }
            },
            new NoteTemplateDef
            {
                Name = "Brainstorm", Icon = "💡", Category = "Ideas", Color = "purple", Tag = "brainstorm",
                Sections = new List<(string, bool)> { ("Topic:", false), ("Ideas:", true), ("Next Steps:", true) }
            },
        };

        private void TemplateButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Style = (Style)FindResource(typeof(ContextMenu));

            // 1. Built-in templates
            var builtInHeader = new MenuItem { Header = "Built-in Templates", IsEnabled = false, FontWeight = FontWeights.Bold };
            menu.Items.Add(builtInHeader);

            foreach (var template in NoteTemplates)
            {
                var item = new MenuItem { Header = $"{template.Icon}  {template.Name}" };
                item.Click += (s, args) => CreateNoteFromTemplate(template);
                menu.Items.Add(item);
            }

            // 2. User templates (if any)
            var userTemplates = DatabaseHelper.ListTemplates();
            if (userTemplates.Count > 0)
            {
                menu.Items.Add(new Separator());
                var userHeader = new MenuItem { Header = "My Templates", IsEnabled = false, FontWeight = FontWeights.Bold };
                menu.Items.Add(userHeader);

                foreach (var note in userTemplates)
                {
                    string snippet = note.Title.Length > 22 ? note.Title.Substring(0, 20) + "…" : note.Title;
                    var item = new MenuItem { Header = $"📝  {snippet}" };
                    int nid = note.Id;
                    item.Click += (s, args) => CreateNoteFromUserTemplate(nid);
                    menu.Items.Add(item);
                }
            }

            // 3. More/Manage option
            menu.Items.Add(new Separator());
            var moreItem = new MenuItem { Header = "📋  Manage Templates..." };
            moreItem.Click += (s, args) => OpenTemplatePicker();
            menu.Items.Add(moreItem);

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private void OpenClipboardPicker()
        {
            if (_clipboardPickerWnd == null || !_clipboardPickerWnd.IsLoaded)
            {
                _clipboardPickerWnd = new ClipboardPickerWindow(this) { Owner = this };
                _clipboardPickerWnd.Show();
            }
            else
            {
                _clipboardPickerWnd.Activate();
            }
        }

        private void CreateNoteFromTemplate(NoteTemplateDef template)
        {
            if (template.Name == "Blank Note")
            {
                CreateNewNote();
                return;
            }

            if (template.Name == "Meeting Notes")
            {
                CreateQuickMeetingNote();
                return;
            }

            var dialog = new InputDialog("Enter a title for this note:", template.Name) { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            string title = $"{template.Name} - {dialog.Answer.Trim()}";
            string contentXaml = BuildTemplateNoteXaml(title, template.Sections);

            int noteId = DatabaseHelper.CreateNote(title, contentXaml, null, null, template.Color);

            var note = DatabaseHelper.GetNote(noteId);
            if (note != null)
            {
                note.Category = template.Category;
                DatabaseHelper.UpdateNote(note);
            }
            if (!string.IsNullOrEmpty(template.Tag))
            {
                DatabaseHelper.AddTagToNote(noteId, template.Tag);
            }

            RefreshNotesList();
            RefreshTagsFilter();
            OpenNoteWindow(noteId);
            ShowStatusToast($"Created {template.Name.ToLower()} note: {title}");
        }

        private static string BuildTemplateNoteXaml(string title, List<(string Heading, bool Bulleted)> sections)
        {
            var titleParagraph = new Paragraph(new Run(title)) { FontWeight = FontWeights.Bold, FontSize = 18, Margin = new Thickness(0, 0, 0, 8) };

            var document = new FlowDocument(titleParagraph)
            {
                // See BuildMeetingNoteXaml for why Foreground/FontFamily must be set explicitly
                // on any standalone FlowDocument not hosted in the live RichTextBox.
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif")
            };

            for (int i = 0; i < sections.Count; i++)
            {
                var (heading, bulleted) = sections[i];
                document.Blocks.Add(MeetingSectionHeading(heading));
                document.Blocks.Add(bulleted ? (Block)MeetingBulletPlaceholder() : new Paragraph(new Run("")));
                if (i < sections.Count - 1)
                {
                    document.Blocks.Add(MeetingBlankSpacer());
                }
            }

            var range = new TextRange(document.ContentStart, document.ContentEnd);
            return NoteContentHelper.SaveRange(range);
        }

        private void CreateQuickMeetingNote()
        {
            var dialog = new InputDialog("Enter the meeting title:", "New Meeting Note") { Owner = this };
            if (dialog.ShowDialog() != true || string.IsNullOrWhiteSpace(dialog.Answer))
                return;

            DateTime now = DateTime.Now;
            string title = $"Meeting - {dialog.Answer.Trim()} ({now:MMM d, yyyy})";
            string contentXaml = BuildMeetingNoteXaml(title, now);

            int noteId = DatabaseHelper.CreateNote(title, contentXaml, null, null, "blue");

            var note = DatabaseHelper.GetNote(noteId);
            if (note != null)
            {
                note.Category = "Meetings";
                // Meeting notes have several sections (Date/Time, Attendees, Discussions, Action
                // Items) that don't fit comfortably in the app's default 300x320 note size, so open
                // them noticeably larger from the start.
                note.W = 420;
                note.H = 520;
                DatabaseHelper.UpdateNote(note);
            }
            DatabaseHelper.AddTagToNote(noteId, "meeting");

            RefreshNotesList();
            RefreshTagsFilter();
            OpenNoteWindow(noteId);
            ShowStatusToast($"Created meeting note: {title}");
        }

        private static Paragraph MeetingSectionHeading(string text) =>
            new Paragraph(new Run(text)) { FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 3) };

        private static Paragraph MeetingBlankSpacer() => new Paragraph(new Run("")) { Margin = new Thickness(0) };

        private static List MeetingBulletPlaceholder(string text = "") =>
            new List(new ListItem(new Paragraph(new Run(text)))) { MarkerStyle = TextMarkerStyle.Disc, Margin = new Thickness(0, 0, 0, 0) };

        private static string BuildMeetingNoteXaml(string title, DateTime meetingTime)
        {
            var titleParagraph = new Paragraph(new Run(title)) { FontWeight = FontWeights.Bold, FontSize = 18, Margin = new Thickness(0, 0, 0, 8) };

            var document = new FlowDocument(titleParagraph)
            {
                // A standalone FlowDocument (not hosted inside a live RichTextBox) has no ambient
                // white text/font to inherit, so its defaults (black, serif "Georgia") get baked into
                // every child Run when serialized -- unreadable and off-brand on the app's dark,
                // sans-serif note UI. Setting them explicitly here fixes both for every paragraph.
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif")
            };

            document.Blocks.Add(MeetingSectionHeading("Date/Time:"));
            document.Blocks.Add(MeetingBulletPlaceholder(meetingTime.ToString("dddd, MMMM d, yyyy 'at' h:mm tt")));
            document.Blocks.Add(MeetingBlankSpacer());

            document.Blocks.Add(MeetingSectionHeading("Attendees:"));
            document.Blocks.Add(MeetingBulletPlaceholder());
            document.Blocks.Add(MeetingBlankSpacer());

            document.Blocks.Add(MeetingSectionHeading("Key Discussions and Decisions:"));
            document.Blocks.Add(MeetingBulletPlaceholder());
            document.Blocks.Add(MeetingBlankSpacer());

            document.Blocks.Add(MeetingSectionHeading("Action Items:"));
            document.Blocks.Add(MeetingBulletPlaceholder());

            var range = new TextRange(document.ContentStart, document.ContentEnd);
            return NoteContentHelper.SaveRange(range);
        }

        private DispatcherTimer? _statusToastTimer;

        public void ShowStatusToast(string message, int durationMs = 2800)
        {
            StatusToastText.Text = message;
            StatusToast.Visibility = Visibility.Visible;

            _statusToastTimer?.Stop();
            _statusToastTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
            _statusToastTimer.Tick += (s, e) =>
            {
                StatusToast.Visibility = Visibility.Collapsed;
                _statusToastTimer?.Stop();
            };
            _statusToastTimer.Start();
        }

        #endregion

        #region Control Event Handlers

        private void NewNoteButton_Click(object sender, RoutedEventArgs e) => CreateNewNote();

        private void ScreenshotButton_Click(object sender, RoutedEventArgs e) => TakeRegionScreenshot();

        private void SaveTabsButton_Click(object sender, RoutedEventArgs e) => SaveBrowserTabs();

        private void AddFileButton_Click(object sender, RoutedEventArgs e) => SaveFilesToNewNote();

        private void MeetingNoteButton_Click(object sender, RoutedEventArgs e) => CreateQuickMeetingNote();

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Style = (Style)FindResource(typeof(ContextMenu));

            var settingsItem = new MenuItem { Header = "⚙  App Settings" };
            settingsItem.Click += (s, args) =>
            {
                var settingsWnd = new SettingsWindow { Owner = this };
                settingsWnd.ShowDialog();
                LoadSavedOpacity();
            };
            menu.Items.Add(settingsItem);

            var graphItem = new MenuItem { Header = "🕸  Tag Mind Graph" };
            graphItem.Click += (s, args) =>
            {
                GraphButton_Click(this, new RoutedEventArgs());
            };
            menu.Items.Add(graphItem);

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshNotesList();

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
                    // Clean image
                    var note = DatabaseHelper.GetNote(id);
                    if (note != null && !string.IsNullOrEmpty(note.ImagePath) && File.Exists(note.ImagePath))
                    {
                        try { File.Delete(note.ImagePath); } catch {}
                    }

                    DatabaseHelper.DeleteNote(id);
                    
                    // Close note window if open
                    if (_openNoteWindows.TryGetValue(id, out NoteWindow? noteWindow))
                    {
                        noteWindow.Close();
                    }

                    RefreshNotesList();
                    RefreshTagsFilter();
                }
            }
        }

        #endregion

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Close all note windows
            var openWindows = _openNoteWindows.Values.ToList();
            foreach (var wnd in openWindows)
            {
                wnd.Close();
            }

            try
            {
                _spotlightWnd?.Close();
                _quickCaptureWnd?.Close();
            }
            catch {}

            // Unregister AppBar
            UnregisterAppBar();

            // Unregister hotkeys
            ComponentDispatcher.ThreadFilterMessage -= ComponentDispatcher_ThreadFilterMessage;
            UnregisterAllHotKeys();

            // Stop clipboard timer
            _clipboardTimer?.Stop();
        }

        #region Clipboard Monitoring & Actions

        private void StartClipboardMonitor()
        {
            _clipboardTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clipboardTimer.Tick += ClipboardTimer_Tick;
            _clipboardTimer.Start();
        }

        private void ClipboardTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                // The clipboard's sequence number only increments when its content actually
                // changes, so this is the one reliable gate for "is this a new copy" -- comparing
                // GetImage() results directly doesn't work, since it returns a fresh BitmapSource
                // instance every call even when nothing changed, causing the same image to be
                // re-added once per poll tick for as long as it stayed on the clipboard.
                int currentSequence = Win32Helper.GetClipboardSequenceNumber();
                if (currentSequence == _lastClipboardSequence) return;
                _lastClipboardSequence = currentSequence;

                if (Clipboard.ContainsText())
                {
                    string currentText = Clipboard.GetText().Trim();
                    if (!string.IsNullOrEmpty(currentText))
                    {
                        AddClipboardItem(new ClipboardHistoryItem
                        {
                            Snippet = currentText.Length > 60 ? currentText.Substring(0, 57) + "..." : currentText,
                            FullText = currentText
                        });
                    }
                }
                else if (Clipboard.ContainsImage())
                {
                    var currentImage = Clipboard.GetImage();
                    if (currentImage != null)
                    {
                        AddClipboardItem(new ClipboardHistoryItem
                        {
                            Snippet = "Clipboard Image",
                            ImageSource = currentImage
                        });
                    }
                }
            }
            catch
            {
                // Clipboard access can throw if occupied by another process, ignore and retry next second
            }
        }

        private void AddClipboardItem(ClipboardHistoryItem item)
        {
            _clipboardHistory.Insert(0, item);
            if (_clipboardHistory.Count > 15)
            {
                _clipboardHistory.RemoveAt(_clipboardHistory.Count - 1);
            }

            _clipboardPickerWnd?.RefreshItems();
        }

        public IReadOnlyList<ClipboardHistoryItem> ClipboardHistory => _clipboardHistory;

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

        // Shared by the "+ From Clipboard" picker window -- converts a clipboard history entry
        // into a real note, with the same OCR/AI auto-tagging clipboard items always got.
        public async Task<int> CreateNoteFromClipboardItemAsync(ClipboardHistoryItem item)
        {
            int noteId;
            if (item.ImageSource != null)
            {
                string filename = $"clipboard_{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString().Substring(0, 6)}.png";
                string filepath = System.IO.Path.Combine(AppConfig.ImagesDir, filename);

                using (var fileStream = new FileStream(filepath, FileMode.Create))
                {
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(item.ImageSource));
                    encoder.Save(fileStream);
                }

                var ocrResult = await OcrHelper.PerformOcrAsync(filepath);
                noteId = DatabaseHelper.CreateNote("Clipboard Image", "", filepath, ocrResult.Text, "yellow");

                var tags = new HashSet<string>(ocrResult.Tags);
                if (await AiHelper.IsOllamaRunningAsync())
                {
                    var aiTags = await AiHelper.AutoTagTextAsync(ocrResult.Text);
                    foreach (var tag in aiTags)
                    {
                        tags.Add(tag.Trim().ToLower());
                    }
                }

                foreach (var tag in tags)
                {
                    DatabaseHelper.AddTagToNote(noteId, tag);
                }
            }
            else
            {
                noteId = DatabaseHelper.CreateNote("Clipboard Text", item.FullText ?? "", null, null, "yellow");

                var tags = new HashSet<string>();
                if (item.FullText != null)
                {
                    if (item.FullText.Contains("http://") || item.FullText.Contains("https://")) tags.Add("link");
                    if (item.FullText.Contains("@")) tags.Add("contact");

                    if (await AiHelper.IsOllamaRunningAsync())
                    {
                        var aiTags = await AiHelper.AutoTagTextAsync(item.FullText);
                        foreach (var tag in aiTags)
                        {
                            tags.Add(tag.Trim().ToLower());
                        }
                    }
                }

                foreach (var tag in tags)
                {
                    DatabaseHelper.AddTagToNote(noteId, tag);
                }
            }

            RefreshNotesList();
            RefreshTagsFilter();
            return noteId;
        }

        #endregion

        #region Sidebar Context Menu Handlers

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

            // List all distinct categories from the database
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

            // New Category option
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

        #endregion
    }

    // View Model for Clipboard Items
    public class ClipboardHistoryItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Snippet { get; set; } = "";
        public string? FullText { get; set; }
        public BitmapSource? ImageSource { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public Visibility TextVisibility => !string.IsNullOrEmpty(FullText) ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ImageVisibility => ImageSource != null ? Visibility.Visible : Visibility.Collapsed;

        public string TimeDisplay => $"{Timestamp:HH:mm:ss} ({GetTimeAgo()})";

        private string GetTimeAgo()
        {
            var span = DateTime.Now - Timestamp;
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            return $"{(int)span.TotalHours}h ago";
        }
    }

    // View Model for Card Items
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

        // U+FE0F (variation selector-16) forces color-emoji presentation; without it WPF's
        // font-linking sometimes falls back to a font with no glyph for these two codepoints
        // and renders a tofu box instead of the icon.
        public string QuickOpenIcon => (QuickOpenItems.Any(i => i.IsFile) ? "📎" : "🔗") + "️";

        public string QuickOpenToolTip => QuickOpenItems.Count == 1
            ? $"Open {QuickOpenItems[0].Label}"
            : $"Open ({QuickOpenItems.Count} links/files)";

        public string TagsList => Tags.Count > 0 ? string.Join("  ", Tags.Select(t => $"#{t}")) : "";

        public Visibility ImageVisibility => (!string.IsNullOrEmpty(ImagePath) && File.Exists(ImagePath)) ? Visibility.Visible : Visibility.Collapsed;

        // --- Task Checklist Properties ---
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
                if (string.IsNullOrEmpty(ImagePath) || !File.Exists(ImagePath)) return null;
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 100; // Small decode height to save memory
                    bitmap.UriSource = new Uri(ImagePath);
                    bitmap.EndInit();
                    return bitmap;
                }
                catch
                {
                    return null;
                }
            }
        }

        // Custom mapping colors for view cards
        private static readonly Dictionary<string, (string bg, string border, string text)> ColorsConfig = 
            new Dictionary<string, (string bg, string border, string text)>
        {
            { "yellow", ("#CC221C12", "#D49A13", "#ffffff") },
            { "green", ("#CC122018", "#1A8F54", "#ffffff") },
            { "pink", ("#CC221218", "#C2185B", "#ffffff") },
            { "purple", ("#CC1B1220", "#7B1FA2", "#ffffff") },
            { "blue", ("#CC121C22", "#0288D1", "#ffffff") },
            { "charcoal", ("#CC1B1B1B", "#424242", "#ffffff") }
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

    public class NoteOrganizationResult
    {
        public string category { get; set; } = "General";
        public List<string> tags { get; set; } = new List<string>();
    }
}