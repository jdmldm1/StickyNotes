using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Speech.Recognition;
using System.Text;

namespace StickyNotes__
{
    public static class AudioDictationHelper
    {
        private static SpeechRecognitionEngine? _speechEngine;
        private static bool _isDictating = false;
        private static bool _isRecordingMemo = false;
        private static string? _activeMemoPath;

        [DllImport("winmm.dll", EntryPoint = "mciSendStringA", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int mciSendString(string lpstrCommand, StringBuilder? lpstrReturnString, int uReturnLength, IntPtr hwndCallback);

        public static bool IsDictating => _isDictating;
        public static bool IsRecordingMemo => _isRecordingMemo;

        public static bool StartDictation(Action<string> onTextRecognized, out string errorMessage)
        {
            errorMessage = "";
            try
            {
                if (_isDictating) StopDictation();

                _speechEngine = new SpeechRecognitionEngine();
                _speechEngine.SetInputToDefaultAudioDevice();
                _speechEngine.LoadGrammar(new DictationGrammar());

                _speechEngine.SpeechRecognized += (s, e) =>
                {
                    if (e.Result != null && !string.IsNullOrWhiteSpace(e.Result.Text))
                    {
                        onTextRecognized(e.Result.Text);
                    }
                };

                _speechEngine.RecognizeAsync(RecognizeMode.Multiple);
                _isDictating = true;
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = "Dictation notice: " + ex.Message;
                _isDictating = false;
                _speechEngine = null;
                return false;
            }
        }

        public static void StopDictation()
        {
            try
            {
                if (_speechEngine != null)
                {
                    _speechEngine.RecognizeAsyncStop();
                    _speechEngine.Dispose();
                    _speechEngine = null;
                }
            }
            catch { }
            finally
            {
                _isDictating = false;
            }
        }

        public static bool StartVoiceMemo(string savePath)
        {
            try
            {
                if (_isRecordingMemo) StopVoiceMemo();

                string? dir = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                _activeMemoPath = savePath;
                mciSendString("close recsound", null, 0, IntPtr.Zero);
                mciSendString("open new type waveaudio alias recsound", null, 0, IntPtr.Zero);
                mciSendString("record recsound", null, 0, IntPtr.Zero);
                _isRecordingMemo = true;
                return true;
            }
            catch
            {
                _isRecordingMemo = false;
                return false;
            }
        }

        public static string? StopVoiceMemo()
        {
            if (!_isRecordingMemo || string.IsNullOrEmpty(_activeMemoPath)) return null;

            try
            {
                mciSendString($"save recsound \"{_activeMemoPath}\"", null, 0, IntPtr.Zero);
                mciSendString("close recsound", null, 0, IntPtr.Zero);
                string path = _activeMemoPath;
                _activeMemoPath = null;
                _isRecordingMemo = false;
                return File.Exists(path) ? path : null;
            }
            catch
            {
                _isRecordingMemo = false;
                return null;
            }
        }

        public static void PlayAudioFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;
                mciSendString("close memoaudio", null, 0, IntPtr.Zero);
                mciSendString($"open \"{filePath}\" type waveaudio alias memoaudio", null, 0, IntPtr.Zero);
                mciSendString("play memoaudio", null, 0, IntPtr.Zero);
            }
            catch { }
        }
    }
}
