using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace StickyNotes__
{
    public static class OcrHelper
    {
        public static async Task<(string Text, List<string> Tags)> PerformOcrAsync(string imagePath)
        {
            if (!File.Exists(imagePath))
                return ("", new List<string>());

            try
            {
                // Open storage file via WinRT
                StorageFile file = await StorageFile.GetFileFromPathAsync(imagePath);
                
                using (IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read))
                {
                    // Decode image
                    BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
                    SoftwareBitmap bitmap = await decoder.GetSoftwareBitmapAsync();

                    // Create OCR Engine
                    OcrEngine engine = OcrEngine.TryCreateFromUserProfileLanguages();
                    if (engine == null)
                    {
                        Console.WriteLine("WinRT OcrEngine is not available on this system.");
                        return ("", new List<string>());
                    }

                    // Run recognition
                    OcrResult result = await engine.RecognizeAsync(bitmap);
                    string text = result.Text;

                    // Generate tags
                    List<string> tags = AutoTagText(text);

                    return (text, tags);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error performing WinRT OCR: " + ex.Message);
                return ("", new List<string>());
            }
        }

        private static List<string> AutoTagText(string text)
        {
            var tags = new HashSet<string>();

            if (string.IsNullOrEmpty(text))
                return new List<string>();

            // 1. Look for URLs
            if (Regex.IsMatch(text, @"https?://[^\s/$.?#].[^\s]*", RegexOptions.IgnoreCase))
            {
                tags.Add("link");
            }

            // 2. Look for Emails
            if (Regex.IsMatch(text, @"[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}"))
            {
                tags.Add("contact");
            }

            // 3. Look for Jira issue keys (e.g. PROJ-1234)
            var jiraMatches = Regex.Matches(text, @"\b([A-Z]{2,10})-\d+\b");
            foreach (Match match in jiraMatches)
            {
                tags.Add(match.Groups[1].Value.ToLower());
                tags.Add("task");
            }

            // 4. Common keywords
            var keywords = new Dictionary<string, string[]>
            {
                { "error", new[] { "error", "exception", "failed", "crash", "bug" } },
                { "setup", new[] { "install", "setup", "configure", "config" } },
                { "credentials", new[] { "password", "username", "token", "apikey", "login", "secret" } },
                { "meeting", new[] { "meeting", "agenda", "sync", "standup", "discuss" } },
                { "database", new[] { "sql", "database", "query", "select", "insert", "update", "db" } },
                { "code", new[] { "def ", "class ", "function", "import ", "const ", "let ", "var " } },
                { "important", new[] { "todo", "fixme", "urgent", "important", "critical" } }
            };

            foreach (var kvp in keywords)
            {
                foreach (var word in kvp.Value)
                {
                    if (Regex.IsMatch(text, @"\b" + Regex.Escape(word) + @"\b", RegexOptions.IgnoreCase))
                    {
                        tags.Add(kvp.Key);
                        break;
                    }
                }
            }

            return new List<string>(tags);
        }
    }
}
