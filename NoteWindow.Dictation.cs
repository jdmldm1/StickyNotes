using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace StickyNotes__
{
    public partial class NoteWindow : Window
    {
        private string? _activeRecordingMemoFile;

        private void ToggleAudioBar_Click(object sender, RoutedEventArgs e)
        {
            if (AudioBar.Visibility == Visibility.Visible)
            {
                AudioBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                AudioBar.Visibility = Visibility.Visible;
                AudioStatusText.Text = "100% Offline Speech Engine Ready";
            }
        }

        private void CloseAudioBar_Click(object sender, RoutedEventArgs e)
        {
            if (AudioDictationHelper.IsDictating)
            {
                AudioDictationHelper.StopDictation();
                DictateToggleBtn.Content = "🎤 Live Dictate";
                DictateToggleBtn.Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x25, 0x12));
            }

            if (AudioDictationHelper.IsRecordingMemo)
            {
                AudioDictationHelper.StopVoiceMemo();
                VoiceMemoRecordBtn.Content = "⏺ Record Memo (.wav)";
                VoiceMemoRecordBtn.Background = new SolidColorBrush(Color.FromRgb(0x3d, 0x14, 0x14));
            }

            AudioBar.Visibility = Visibility.Collapsed;
        }

        private void DictateToggle_Click(object sender, RoutedEventArgs e)
        {
            if (AudioDictationHelper.IsDictating)
            {
                AudioDictationHelper.StopDictation();
                DictateToggleBtn.Content = "🎤 Live Dictate";
                DictateToggleBtn.Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x25, 0x12));
                AudioStatusText.Text = "Dictation stopped.";
                return;
            }

            if (AudioDictationHelper.IsRecordingMemo)
            {
                AudioDictationHelper.StopVoiceMemo();
                VoiceMemoRecordBtn.Content = "⏺ Record Memo (.wav)";
                VoiceMemoRecordBtn.Background = new SolidColorBrush(Color.FromRgb(0x3d, 0x14, 0x14));
            }

            bool success = AudioDictationHelper.StartDictation(text =>
            {
                Dispatcher.Invoke(() =>
                {
                    if (string.IsNullOrWhiteSpace(text)) return;

                    var caret = NoteRichTextBox.CaretPosition;
                    var insertRange = new TextRange(caret, caret);
                    insertRange.Text = text + " ";
                    NoteRichTextBox.CaretPosition = insertRange.End;
                    AudioStatusText.Text = $"Transcribed: \"{text}\"";
                });
            }, out string errorMsg);

            if (success)
            {
                DictateToggleBtn.Content = "⏹ Stop Dictating";
                DictateToggleBtn.Background = new SolidColorBrush(Color.FromRgb(0x8a, 0x3b, 0x12));
                AudioStatusText.Text = "🎤 Listening (100% Offline Windows Engine)...";
            }
            else
            {
                AudioStatusText.Text = string.IsNullOrEmpty(errorMsg) ? "Could not initialize audio input device." : errorMsg;
            }
        }

        private async void VoiceMemoRecord_Click(object sender, RoutedEventArgs e)
        {
            if (AudioDictationHelper.IsRecordingMemo)
            {
                string? savedPath = AudioDictationHelper.StopVoiceMemo();
                VoiceMemoRecordBtn.Content = "⏺ Record Memo (.wav)";
                VoiceMemoRecordBtn.Background = new SolidColorBrush(Color.FromRgb(0x3d, 0x14, 0x14));

                if (!string.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
                {
                    await DatabaseHelper.AddAttachmentAsync(_noteId, savedPath);
                    RefreshAttachmentsPanel();
                    NotifyNotesChanged();
                    AudioStatusText.Text = $"Saved voice memo: {Path.GetFileName(savedPath)}";
                }
                else
                {
                    AudioStatusText.Text = "Voice memo recording ended.";
                }
                _activeRecordingMemoFile = null;
                return;
            }

            if (AudioDictationHelper.IsDictating)
            {
                AudioDictationHelper.StopDictation();
                DictateToggleBtn.Content = "🎤 Live Dictate";
                DictateToggleBtn.Background = new SolidColorBrush(Color.FromRgb(0x3a, 0x25, 0x12));
            }

            string memoPath = Path.Combine(AppConfig.AttachmentsDir, $"memo_note{_noteId}_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
            _activeRecordingMemoFile = memoPath;

            bool started = AudioDictationHelper.StartVoiceMemo(memoPath);
            if (started)
            {
                VoiceMemoRecordBtn.Content = "⏹ Save Memo";
                VoiceMemoRecordBtn.Background = new SolidColorBrush(Color.FromRgb(0x8b, 0x1a, 0x1a));
                AudioStatusText.Text = "⏺ Recording voice memo... Click Save when done";
            }
            else
            {
                AudioStatusText.Text = "Failed to start audio recording device.";
                _activeRecordingMemoFile = null;
            }
        }
    }
}
