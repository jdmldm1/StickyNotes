using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Navigation;

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
            var list = new List<(string, string)>();
            if (string.IsNullOrEmpty(content)) return list;

            try
            {
                var doc = new FlowDocument();
                if (TryLoadRange(new TextRange(doc.ContentStart, doc.ContentEnd), content))
                {
                    CollectHyperlinks(doc.Blocks, list);
                }
            }
            catch {}
            return list;
        }

        /// <summary>
        /// Extracts all [[Title]] wiki-link references from a plain-text string.
        /// Returns the referenced titles (without the [[ ]] brackets).
        /// </summary>
        public static List<string> ExtractWikiLinks(string plainText)
        {
            var results = new List<string>();
            if (string.IsNullOrEmpty(plainText)) return results;

            var matches = System.Text.RegularExpressions.Regex.Matches(
                plainText, @"\[\[([^\]]+)\]\]");
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                string title = m.Groups[1].Value.Trim();
                if (!string.IsNullOrEmpty(title))
                    results.Add(title);
            }
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

        // Shared by any window that hosts a live RichTextBox for a loaded note (NoteWindow,
        // NoteManagerWindow): event handlers and hardcoded colors don't survive XAML
        // (de)serialization, so hyperlinks and any baked-in black text need repairing after load.
        public static void RewireInteractiveElements(FlowDocument document, RequestNavigateEventHandler navigateHandler)
        {
            RewireBlocks(document.Blocks, navigateHandler);
        }

        private static void RewireBlocks(BlockCollection blocks, RequestNavigateEventHandler navigateHandler)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        RewireInlines(paragraph.Inlines, navigateHandler);
                        break;
                    case List list:
                        foreach (var item in list.ListItems)
                            RewireBlocks(item.Blocks, navigateHandler);
                        break;
                    case Section section:
                        RewireBlocks(section.Blocks, navigateHandler);
                        break;
                }
            }
        }

        private static void RewireInlines(InlineCollection inlines, RequestNavigateEventHandler navigateHandler)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Hyperlink hyperlink:
                        hyperlink.RequestNavigate -= navigateHandler;
                        hyperlink.RequestNavigate += navigateHandler;
                        RewireInlines(hyperlink.Inlines, navigateHandler);
                        break;
                    case Span span:
                        NormalizeForegroundIfBlack(span);
                        RewireInlines(span.Inlines, navigateHandler);
                        break;
                    case Run run:
                        NormalizeForegroundIfBlack(run);
                        break;
                }
            }
        }

        private static void NormalizeForegroundIfBlack(Inline inline)
        {
            if (inline.Foreground is System.Windows.Media.SolidColorBrush brush && brush.Color == System.Windows.Media.Colors.Black)
            {
                inline.Foreground = System.Windows.Media.Brushes.White;
            }
        }

        // Checklist items are plain-text Unicode glyphs (not embedded controls) because
        // BlockUIContainer content doesn't survive TextRange.Save -- see NoteWindow's checklist
        // handling for the original implementation this mirrors, shared here for NoteManagerWindow.
        public const string UncheckedGlyph = "☐ ";
        public const string CheckedGlyph = "☑ ";

        public static Run? GetChecklistRun(Paragraph? paragraph)
        {
            if (paragraph?.Inlines.FirstInline is Run run &&
                (run.Text.StartsWith(UncheckedGlyph, StringComparison.Ordinal) || run.Text.StartsWith(CheckedGlyph, StringComparison.Ordinal)))
                return run;
            return null;
        }

        public static Run? GetChecklistRunAt(TextPointer? position)
        {
            if (position == null) return null;
            var run = position.Parent as Run ?? position.GetAdjacentElement(LogicalDirection.Forward) as Run;
            if (run == null) return null;

            var checklistRun = GetChecklistRun(run.Parent as Paragraph);
            if (checklistRun != run) return null;

            // Only toggle when the click actually lands within the glyph itself, not the text after it.
            var glyphEnd = run.ContentStart.GetPositionAtOffset(UncheckedGlyph.Length);
            if (glyphEnd == null || position.CompareTo(glyphEnd) > 0) return null;

            return checklistRun;
        }

        public static void ToggleChecklistItem(Run checklistRun)
        {
            bool wasChecked = checklistRun.Text.StartsWith(CheckedGlyph, StringComparison.Ordinal);
            string rest = checklistRun.Text.Substring(UncheckedGlyph.Length);
            checklistRun.Text = (wasChecked ? UncheckedGlyph : CheckedGlyph) + rest;

            var decoration = wasChecked ? null : TextDecorations.Strikethrough;
            var foreground = wasChecked ? System.Windows.Media.Brushes.White : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x88, 0x88, 0x88));
            checklistRun.TextDecorations = decoration;
            checklistRun.Foreground = foreground;

            if (checklistRun.Parent is Paragraph paragraph)
            {
                foreach (var inline in paragraph.Inlines)
                {
                    if (inline == checklistRun) continue;
                    inline.TextDecorations = decoration;
                    inline.Foreground = foreground;
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
