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
        private SpotlightWindow? _spotlightWnd;
        private QuickCaptureWindow? _quickCaptureWnd;
        private NoteManagerWindow? _noteManagerWnd;
        private GraphWindow? _graphWnd;
        private TemplatePickerWindow? _templatePickerWnd;
        private readonly Dictionary<int, NoteWindow> _openNoteWindows = new Dictionary<int, NoteWindow>();
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
                CreateNewNote();
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
            new NoteTemplateDef { Name = "Meeting Notes", Icon = "🗓️" },
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

            var builtInHeader = new MenuItem { Header = "Built-in Templates", IsEnabled = false, FontWeight = FontWeights.Bold };
            menu.Items.Add(builtInHeader);

            foreach (var template in NoteTemplates)
            {
                var item = new MenuItem { Header = $"{template.Icon}  {template.Name}" };
                item.Click += (s, args) => CreateNoteFromTemplate(template);
                menu.Items.Add(item);
            }

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

            menu.Items.Add(new Separator());
            var moreItem = new MenuItem { Header = "📋  Manage Templates..." };
            moreItem.Click += (s, args) => OpenTemplatePicker();
            menu.Items.Add(moreItem);

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
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

        private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => DebounceSearch();
    }
}
