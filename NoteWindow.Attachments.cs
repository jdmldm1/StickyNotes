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
        private static string GetFileGlyph(string fileName)
        {
            string ext = Path.GetExtension(fileName).ToLowerInvariant();
            return ext switch
            {
                ".pdf" => "📕",
                ".doc" or ".docx" => "📝",
                ".xls" or ".xlsx" or ".csv" => "📊",
                ".ppt" or ".pptx" => "📽",
                ".zip" or ".rar" or ".7z" => "🗜",
                ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" => "🖼",
                ".mp3" or ".wav" or ".flac" => "🎵",
                ".mp4" or ".mov" or ".avi" or ".mkv" => "🎬",
                _ => "📎"
            };
        }
        private void RefreshAttachmentsPanel()
        {
            AttachmentsPanel.Children.Clear();
            var attachments = DatabaseHelper.GetNoteAttachments(_noteId);

            foreach (var attachment in attachments)
            {
                AttachmentsPanel.Children.Add(CreateAttachmentChip(attachment));
            }

            var addChip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 8, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand
            };
            addChip.Child = new TextBlock { Text = "+ Add File", FontSize = 10.5, Foreground = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)) };
            addChip.MouseLeftButtonUp += (s, e) => AddFilesViaDialog();
            AttachmentsPanel.Children.Add(addChip);
        }
        private Border CreateAttachmentChip(NoteAttachment attachment)
        {
            var chip = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 5, 6, 5),
                Margin = new Thickness(0, 0, 6, 6),
                Cursor = Cursors.Hand,
                ToolTip = attachment.FileName
            };

            var stack = new StackPanel { Orientation = Orientation.Horizontal };
            stack.Children.Add(new TextBlock { Text = GetFileGlyph(attachment.FileName), FontSize = 12, Margin = new Thickness(0, 0, 5, 0), VerticalAlignment = VerticalAlignment.Center });
            stack.Children.Add(new TextBlock
            {
                Text = attachment.FileName,
                FontSize = 10.5,
                Foreground = Brushes.White,
                MaxWidth = 120,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            });

            var removeButton = new TextBlock
            {
                Text = " ✕",
                FontSize = 9.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0xff, 0x52, 0x52)),
                Margin = new Thickness(6, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand
            };
            removeButton.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                var result = MessageBox.Show($"Remove attachment \"{attachment.FileName}\"?", "Remove Attachment", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    DatabaseHelper.DeleteAttachment(attachment.Id);
                    RefreshAttachmentsPanel();
                }
            };
            stack.Children.Add(removeButton);

            chip.Child = stack;
            chip.MouseLeftButtonUp += (s, e) =>
            {
                if (e.OriginalSource == removeButton) return;
                OpenAttachment(attachment);
            };

            return chip;
        }
        private static void OpenAttachment(NoteAttachment attachment)
        {
            if (!File.Exists(attachment.FilePath))
            {
                MessageBox.Show("This file no longer exists on disk.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                Process.Start(new ProcessStartInfo(attachment.FilePath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Couldn't open file: {ex.Message}", "Open Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void AddFilesViaDialog()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Attach files to note",
                Multiselect = true,
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) == true)
            {
                AttachFiles(dialog.FileNames);
            }
        }
        private async void AttachFiles(IEnumerable<string> filePaths)
        {
            foreach (var path in filePaths)
            {
                try
                {
                    await DatabaseHelper.AddAttachmentAsync(_noteId, path);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Couldn't attach \"{Path.GetFileName(path)}\": {ex.Message}", "Attach Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            RefreshAttachmentsPanel();
            NotifyNotesChanged();
        }
        private void Editor_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropHintBorder.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }
        private void Editor_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }
        private void Editor_DragLeave(object sender, DragEventArgs e)
        {
            DropHintBorder.Visibility = Visibility.Collapsed;
        }
        private void Editor_Drop(object sender, DragEventArgs e)
        {
            DropHintBorder.Visibility = Visibility.Collapsed;
            if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;

            if (e.Data.GetData(DataFormats.FileDrop) is string[] filePaths && filePaths.Length > 0)
            {
                AttachFiles(filePaths.Where(File.Exists));
            }
            e.Handled = true;
        }
    }
}
