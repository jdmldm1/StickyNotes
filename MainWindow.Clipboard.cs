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
        private ClipboardPickerWindow? _clipboardPickerWnd;
        private System.Windows.Threading.DispatcherTimer? _clipboardTimer;
        private readonly List<ClipboardHistoryItem> _clipboardHistory = new List<ClipboardHistoryItem>();
        private int _lastClipboardSequence = -1;
        public void OpenClipboardPicker()
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
                if (SettingsService.Current.AutoTagNewNotes && await AiHelper.IsOllamaRunningAsync())
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

                    if (SettingsService.Current.AutoTagNewNotes && await AiHelper.IsOllamaRunningAsync())
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
    }

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

}
