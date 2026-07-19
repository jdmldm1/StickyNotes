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
        public async void TakeRegionScreenshot()
        {
            this.WindowState = WindowState.Minimized;

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

                    RestoreFromTray();

                    await System.Threading.Tasks.Task.Delay(150);

                    int noteId = DatabaseHelper.CreateNote(
                        "Screenshot note", 
                        "", 
                        imagePath, 
                        "", 
                        "yellow"
                    );

                    var noteWindow = OpenNoteWindow(noteId);
                    RefreshNotesList();
                    RefreshTagsFilter();

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

                        var tags = new HashSet<string>(ocrTags);
                        try
                        {
                            if (SettingsService.Current.AutoTagNewNotes && !string.IsNullOrEmpty(ocrText) && await AiHelper.IsOllamaRunningAsync())
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

                        foreach (var tag in tags)
                        {
                            if (!string.IsNullOrWhiteSpace(tag))
                                DatabaseHelper.AddTagToNote(noteId, tag);
                        }

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
    }
}
