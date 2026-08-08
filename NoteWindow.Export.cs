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
        private void CopyNoteToClipboard()
        {
            var range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            if (!string.IsNullOrEmpty(range.Text))
            {
                Clipboard.SetText(range.Text);
            }
        }

        private void NoteImage_Click(object sender, MouseButtonEventArgs e) => OpenImageViewer();
        private void OpenImageViewer()
        {
            if (string.IsNullOrEmpty(_note.ImagePath) || !File.Exists(_note.ImagePath)) return;
            var viewer = new ImageViewerWindow(_note.ImagePath) { Owner = this };
            viewer.ShowDialog();
        }
        private void NoteImage_RightClick(object sender, MouseButtonEventArgs e)
        {
            if (string.IsNullOrEmpty(_note.ImagePath) || !File.Exists(_note.ImagePath)) return;

            var menu = new ContextMenu();
            menu.Style = (Style)FindResource(typeof(ContextMenu));

            var viewItem = new MenuItem { Header = "View Full Size" };
            viewItem.Click += (s, args) => OpenImageViewer();
            menu.Items.Add(viewItem);

            var copyItem = new MenuItem { Header = "Copy Image" };
            copyItem.Click += (s, args) => CopyImageToClipboard();
            menu.Items.Add(copyItem);

            var reOcrItem = new MenuItem { Header = "Re-run OCR" };
            reOcrItem.Click += async (s, args) => await ReRunOcrAsync();
            menu.Items.Add(reOcrItem);

            menu.IsOpen = true;
            e.Handled = true;
        }
        private async Task ReRunOcrAsync()
        {
            if (string.IsNullOrEmpty(_note.ImagePath) || !File.Exists(_note.ImagePath)) return;

            var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
            try
            {
                var ocrResult = await OcrHelper.PerformOcrAsync(_note.ImagePath);
                string ocrText = ocrResult.Text ?? "";
                if (string.IsNullOrWhiteSpace(ocrText))
                {
                    main?.ShowStatusToast("No text found in this image");
                    return;
                }

                _note.OcrText = ocrText;

                string currentPlainText = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd).Text.Trim();
                if (string.IsNullOrEmpty(currentPlainText))
                {
                    var doc = new FlowDocument();
                    doc.Blocks.Add(new Paragraph(new Run(ocrText)));
                    var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                    _note.Content = NoteContentHelper.SaveRange(range);
                }

                DatabaseHelper.UpdateNote(_note);

                foreach (var tag in ocrResult.Tags)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                        DatabaseHelper.AddTagToNote(_note.Id, tag);
                }

                ReloadNoteFromDb();
                main?.RefreshNotesList();
                main?.RefreshTagsFilter();
                main?.ShowStatusToast("OCR text updated ✓");
            }
            catch (Exception ex)
            {
                MessageBox.Show("OCR failed: " + ex.Message, "Re-run OCR", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private void CopyImageToClipboard()
        {
            if (NoteImage.Source is not BitmapSource src) return;
            try
            {
                Clipboard.SetImage(src);
                var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
                main?.ShowStatusToast("Image copied to clipboard 📋");
            }
            catch
            {
                MessageBox.Show("Couldn't copy the image to the clipboard.", "Copy Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        private void ExportNote(string format)
        {
            bool isDocx = format == "docx";
            string title = string.IsNullOrWhiteSpace(_note.Title) ? "Sticky Note" : _note.Title;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = isDocx ? "Export Note as Word Document" : "Export Note as PDF",
                FileName = SanitizeFileName(title),
                DefaultExt = isDocx ? ".docx" : ".pdf",
                Filter = isDocx ? "Word Document (*.docx)|*.docx" : "PDF Document (*.pdf)|*.pdf"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                var blocks = NoteExportHelper.ExtractExportBlocks(NoteRichTextBox.Document);
                if (isDocx)
                    NoteExportHelper.WriteDocx(dialog.FileName, title, blocks);
                else
                    NoteExportHelper.WritePdf(dialog.FileName, title, blocks);

                MessageBox.Show("Note exported successfully!", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "Sticky Note" : name;
        }
    }
}
