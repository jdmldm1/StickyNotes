using System;
using System.Collections.Generic;
using System.Linq;

namespace StickyNotes__
{
    public enum DiffLineType
    {
        Unchanged,
        Added,
        Deleted
    }

    public class DiffLine
    {
        public DiffLineType Type { get; set; }
        public string Text { get; set; } = "";
        public int? OldLineNumber { get; set; }
        public int? NewLineNumber { get; set; }
    }

    public class DiffSummary
    {
        public int AddedCount { get; set; }
        public int DeletedCount { get; set; }
        public int UnchangedCount { get; set; }
        public int WordDelta { get; set; }
        public int OldWords { get; set; }
        public int NewWords { get; set; }
        public List<DiffLine> Lines { get; set; } = new();
    }

    public static class NoteDiffHelper
    {
        public static string[] SplitLines(string? text)
        {
            if (string.IsNullOrEmpty(text)) return Array.Empty<string>();
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        public static int CountWords(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            return text.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        public static string FormatRelativeTime(DateTime dt)
        {
            var span = DateTime.Now - dt;
            if (span.TotalSeconds < 10) return "Just now";
            if (span.TotalSeconds < 60) return $"{(int)span.TotalSeconds}s ago";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes}m ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours}h ago";
            if (span.TotalDays < 2) return $"Yesterday {dt:t}";
            if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
            return dt.ToString("MMM d, yyyy");
        }

        public static DiffSummary ComputeDiff(string? oldText, string? newText)
        {
            string[] oldLines = SplitLines(oldText);
            string[] newLines = SplitLines(newText);

            int m = oldLines.Length;
            int n = newLines.Length;

            const int maxLines = 1500;
            if (m > maxLines || n > maxLines)
            {
                oldLines = oldLines.Take(maxLines).ToArray();
                newLines = newLines.Take(maxLines).ToArray();
                m = oldLines.Length;
                n = newLines.Length;
            }

            int[,] dp = new int[m + 1, n + 1];
            for (int i = 0; i < m; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (oldLines[i] == newLines[j])
                        dp[i + 1, j + 1] = dp[i, j] + 1;
                    else
                        dp[i + 1, j + 1] = Math.Max(dp[i + 1, j], dp[i, j + 1]);
                }
            }

            var reversed = new List<DiffLine>();
            int rI = m, rJ = n;
            while (rI > 0 || rJ > 0)
            {
                if (rI > 0 && rJ > 0 && oldLines[rI - 1] == newLines[rJ - 1])
                {
                    reversed.Add(new DiffLine
                    {
                        Type = DiffLineType.Unchanged,
                        Text = oldLines[rI - 1],
                        OldLineNumber = rI,
                        NewLineNumber = rJ
                    });
                    rI--;
                    rJ--;
                }
                else if (rJ > 0 && (rI == 0 || dp[rI, rJ - 1] >= dp[rI - 1, rJ]))
                {
                    reversed.Add(new DiffLine
                    {
                        Type = DiffLineType.Added,
                        Text = newLines[rJ - 1],
                        NewLineNumber = rJ
                    });
                    rJ--;
                }
                else if (rI > 0 && (rJ == 0 || dp[rI, rJ - 1] < dp[rI - 1, rJ]))
                {
                    reversed.Add(new DiffLine
                    {
                        Type = DiffLineType.Deleted,
                        Text = oldLines[rI - 1],
                        OldLineNumber = rI
                    });
                    rI--;
                }
            }

            reversed.Reverse();

            int oldWords = CountWords(oldText);
            int newWords = CountWords(newText);

            return new DiffSummary
            {
                Lines = reversed,
                AddedCount = reversed.Count(l => l.Type == DiffLineType.Added),
                DeletedCount = reversed.Count(l => l.Type == DiffLineType.Deleted),
                UnchangedCount = reversed.Count(l => l.Type == DiffLineType.Unchanged),
                WordDelta = newWords - oldWords,
                OldWords = oldWords,
                NewWords = newWords
            };
        }
    }
}
