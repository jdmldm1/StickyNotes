using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;

namespace StickyNotes__
{
    // Centralizes how a note's rich FlowDocument content is serialized to/from the
    // string stored in the database. DataFormats.XamlPackage (not plain Xaml) is
    // required for embedded controls (e.g. checkbox list items) to round-trip --
    // plain Xaml silently drops any BlockUIContainer content on save.
    public static class NoteContentHelper
    {
        public static string SaveRange(TextRange range)
        {
            using var ms = new MemoryStream();
            range.Save(ms, DataFormats.XamlPackage);
            return Convert.ToBase64String(ms.ToArray());
        }

        public static bool TryLoadRange(TextRange range, string? content)
        {
            if (string.IsNullOrEmpty(content)) return false;

            try
            {
                byte[] bytes = Convert.FromBase64String(content);
                using var ms = new MemoryStream(bytes);
                range.Load(ms, DataFormats.XamlPackage);
                return true;
            }
            catch { }

            // Legacy format from older builds / RTF import: plain-text Xaml (no embedded controls).
            try
            {
                using var ms = new MemoryStream(Encoding.UTF8.GetBytes(content));
                range.Load(ms, DataFormats.Xaml);
                return true;
            }
            catch { }

            return false;
        }

        public static List<(string Label, string Url)> ExtractHyperlinks(string? content)
        {
            var results = new List<(string, string)>();
            if (string.IsNullOrEmpty(content)) return results;

            try
            {
                var doc = new FlowDocument();
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                if (TryLoadRange(range, content))
                {
                    CollectHyperlinks(doc.Blocks, results);
                }
            }
            catch { }

            return results;
        }

        private static void CollectHyperlinks(BlockCollection blocks, List<(string, string)> results)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        CollectHyperlinksInInlines(paragraph.Inlines, results);
                        break;
                    case List list:
                        foreach (var item in list.ListItems)
                            CollectHyperlinks(item.Blocks, results);
                        break;
                    case Section section:
                        CollectHyperlinks(section.Blocks, results);
                        break;
                }
            }
        }

        private static void CollectHyperlinksInInlines(InlineCollection inlines, List<(string, string)> results)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Hyperlink hyperlink when hyperlink.NavigateUri != null:
                        var linkRange = new TextRange(hyperlink.ContentStart, hyperlink.ContentEnd);
                        results.Add((linkRange.Text.Trim(), hyperlink.NavigateUri.AbsoluteUri));
                        break;
                    case Span span:
                        CollectHyperlinksInInlines(span.Inlines, results);
                        break;
                }
            }
        }

        public static string ExtractPlainText(string? content)
        {
            if (string.IsNullOrEmpty(content)) return "";

            try
            {
                var doc = new FlowDocument();
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                if (TryLoadRange(range, content))
                    return range.Text.Trim();
            }
            catch { }

            // Last-resort fallback for malformed/unrecognized content: strip tags from raw text.
            try
            {
                string text = Regex.Replace(content, "<[^>]+>", "");
                text = text.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
                return text.Trim();
            }
            catch
            {
                return content;
            }
        }
    }
}
