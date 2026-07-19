using System;
using System.Collections.Generic;
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
using System.Threading.Tasks;

namespace StickyNotes__
{
    public partial class NoteWindow : Window
    {
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
    }
}
