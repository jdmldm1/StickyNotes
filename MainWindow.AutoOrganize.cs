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
        public async Task RunAutoOrganizeAsync()
        {
            try
            {
                if (!await AiHelper.IsOllamaRunningAsync())
                {
                    MessageBox.Show("Please start Ollama (local AI service) to use the Auto Organizer.", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var notes = DatabaseHelper.ListNotes(null, null);
                if (notes.Count == 0)
                {
                    MessageBox.Show("No notes found to organize.", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine("You are a system organizer. Group these notes into 3 to 5 logical category names (e.g. Work, Personal, Code, Finance). Also, assign 1 to 3 relevant, brief tags to each note.");
                sb.AppendLine("Respond with ONLY a raw JSON object where keys are note IDs (as strings) and values are objects containing 'category' (string) and 'tags' (array of strings).");
                sb.AppendLine("Example format:");
                sb.AppendLine("{\n  \"1\": { \"category\": \"Work\", \"tags\": [\"deadline\", \"invoice\"] }\n}");
                sb.AppendLine("Do not include any explanations, markdown code blocks, backticks, or formatting. Strictly JSON.");
                sb.AppendLine("Notes list:");
                foreach (var note in notes)
                {
                    string plain = GetPlainTextFromXaml(note.Content);
                    if (plain.Length > 80) plain = plain.Substring(0, 80);
                    sb.AppendLine($"ID: {note.Id} | Title: {note.Title} | Snippet: {plain}");
                }

                string response = await AiHelper.GenerateTextAsync(sb.ToString());
                if (string.IsNullOrEmpty(response))
                {
                    MessageBox.Show("AI did not respond. Check if Ollama is active.", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                int firstBrace = response.IndexOf('{');
                int lastBrace = response.LastIndexOf('}');
                if (firstBrace == -1 || lastBrace == -1)
                {
                    MessageBox.Show("Could not parse AI response: JSON structure not found.", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string json = response.Substring(firstBrace, lastBrace - firstBrace + 1);
                bool wasTruncated = false;
                Dictionary<string, NoteOrganizationResult>? mappings;
                try
                {
                    mappings = JsonSerializer.Deserialize<Dictionary<string, NoteOrganizationResult>>(json);
                }
                catch (JsonException)
                {
                    string? repaired = TryRepairTruncatedJsonObject(json);
                    mappings = repaired != null ? TryDeserializeMappings(repaired) : null;
                    wasTruncated = mappings != null;
                }

                if (mappings == null)
                {
                    MessageBox.Show(
                        "The AI's response wasn't valid JSON, so no notes were organized. This is more common with smaller/faster models -- try again, or switch to a larger model in Settings for more reliable results.",
                        "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                foreach (var pair in mappings)
                {
                    if (int.TryParse(pair.Key, out int noteId))
                    {
                        var note = notes.FirstOrDefault(n => n.Id == noteId);
                        if (note != null)
                        {
                            note.Category = pair.Value.category?.Trim() ?? "General";
                            DatabaseHelper.UpdateNote(note);

                            DatabaseHelper.ClearNoteTags(noteId);
                            if (pair.Value.tags != null)
                            {
                                foreach (var tag in pair.Value.tags)
                                {
                                    DatabaseHelper.AddTagToNote(noteId, tag);
                                }
                            }
                        }
                    }
                }

                RefreshNotesList();
                RefreshTagsFilter();

                if (wasTruncated || mappings.Count < notes.Count)
                {
                    MessageBox.Show(
                        $"Organized {mappings.Count} of {notes.Count} notes. The AI's response was incomplete (common with smaller/faster models) -- run Auto Organize again to catch the rest.",
                        "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show("Notes successfully organized and tagged!", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Organization error: {ex.Message}", "AI Organizer", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private static Dictionary<string, NoteOrganizationResult>? TryDeserializeMappings(string json)
        {
            try
            {
                return JsonSerializer.Deserialize<Dictionary<string, NoteOrganizationResult>>(json);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        private static string? TryRepairTruncatedJsonObject(string json)
        {
            int depth = 0;
            bool inString = false;
            bool escape = false;
            int lastCompleteEntryEnd = -1;

            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (escape) { escape = false; continue; }
                if (c == '\\' && inString) { escape = true; continue; }
                if (c == '"') { inString = !inString; continue; }
                if (inString) continue;

                if (c == '{' || c == '[')
                {
                    depth++;
                }
                else if (c == '}' || c == ']')
                {
                    depth--;
                    if (depth == 1) lastCompleteEntryEnd = i;
                }
            }

            if (lastCompleteEntryEnd <= 0) return null;
            return json.Substring(0, lastCompleteEntryEnd + 1) + "}";
        }

        private string GetPlainTextFromXaml(string xaml) => NoteContentHelper.ExtractPlainText(xaml);
    }

    public class NoteOrganizationResult
    {
        public string category { get; set; } = "General";
        public List<string> tags { get; set; } = new List<string>();
    }
}
