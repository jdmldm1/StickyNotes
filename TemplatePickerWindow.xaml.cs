using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StickyNotes__
{
    public partial class TemplatePickerWindow : Window
    {
        private readonly MainWindow _mainWnd;

        public TemplatePickerWindow(MainWindow mainWnd)
        {
            InitializeComponent();
            _mainWnd = mainWnd;
            Owner = mainWnd;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            PopulateTemplates();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void PopulateTemplates()
        {
            TemplatesPanel.Children.Clear();

            // Built-in templates
            var builtIns = new[]
            {
                ("\ud83d\udcc4", "Blank Note",     "yellow"),
                ("\ud83d\uddd3\ufe0f", "Meeting Notes",  "blue"),
                ("\ud83d\udde3\ufe0f", "1:1 Meeting",    "blue"),
                ("\u2600\ufe0f", "Daily Standup",  "green"),
                ("\ud83d\udc1b", "Bug Report",     "pink"),
                ("\ud83d\udca1", "Brainstorm",     "purple"),
            };

            foreach (var (icon, name, color) in builtIns)
                TemplatesPanel.Children.Add(BuildTemplateCard(icon, name, color, null));

            // User-defined templates from DB
            var userTemplates = DatabaseHelper.ListTemplates();
            foreach (var note in userTemplates)
            {
                string icon = "\ud83d\udcdd";
                string snippet = note.Title.Length > 18 ? note.Title.Substring(0, 16) + "…" : note.Title;
                TemplatesPanel.Children.Add(BuildTemplateCard(icon, snippet, note.Color, note.Id));
            }
        }

        private Border BuildTemplateCard(string icon, string name, string color, int? noteId)
        {
            var accentColors = new Dictionary<string, Color>()
            {
                { "yellow",  Color.FromRgb(0xD4, 0x9A, 0x13) },
                { "blue",    Color.FromRgb(0x02, 0x88, 0xD1) },
                { "green",   Color.FromRgb(0x1A, 0x8F, 0x54) },
                { "pink",    Color.FromRgb(0xC2, 0x18, 0x5B) },
                { "purple",  Color.FromRgb(0x7B, 0x1F, 0xA2) },
                { "charcoal",Color.FromRgb(0x42, 0x42, 0x42) },
            };
            Color accent = accentColors.TryGetValue(color, out Color c) ? c : accentColors["yellow"];

            var grid = new Grid { Width = 120, Height = 100 };

            var card = new Border
            {
                Width = 120, Height = 100,
                Margin = new Thickness(6),
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(40, accent.R, accent.G, accent.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, accent.R, accent.G, accent.B)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                Tag = noteId,
            };

            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = icon, FontSize = 28, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, FontSize = 11, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, MaxWidth = 100 });
            card.Child = panel;
            card.MouseLeftButtonDown += (s, e) =>
            {
                if (noteId.HasValue)
                    _mainWnd.CreateNoteFromUserTemplate(noteId.Value);
                else
                    _mainWnd.CreateNoteFromBuiltInTemplate(name);
                Close();
            };

            grid.Children.Add(card);

            // Delete button for user templates
            if (noteId.HasValue)
            {
                var delBtn = new Button
                {
                    Content = "✕",
                    Width = 18, Height = 18,
                    Background = new SolidColorBrush(Color.FromArgb(180, 200, 50, 50)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    FontSize = 9,
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 2, 2, 0),
                    ToolTip = "Remove template",
                };
                delBtn.Resources.Add(typeof(Border), new Style(typeof(Border)) { Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) } });
                delBtn.Click += (s, e) =>
                {
                    e.Handled = true;
                    DatabaseHelper.SetNoteIsTemplate(noteId.Value, false);
                    PopulateTemplates();
                };
                grid.Children.Add(delBtn);
            }

            return new Border { Child = grid };
        }
    }
}
