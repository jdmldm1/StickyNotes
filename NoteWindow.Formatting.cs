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
        private void NoteRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (NoteRichTextBox == null || FormatToolbarPopup == null) return;

            if (NoteRichTextBox.Selection.IsEmpty)
            {
                FormatToolbarPopup.IsOpen = false;
            }
            else
            {
                var start = NoteRichTextBox.Selection.Start;
                var end = NoteRichTextBox.Selection.End;

                Rect rectStart = start.GetCharacterRect(LogicalDirection.Forward);
                Rect rectEnd = end.GetCharacterRect(LogicalDirection.Backward);

                double selectionLeft = Math.Min(rectStart.Left, rectEnd.Left);
                double selectionRight = Math.Max(rectStart.Right, rectEnd.Right);
                double selectionTop = rectStart.Top;

                double midpointX = selectionLeft + (selectionRight - selectionLeft) / 2;

                FormatToolbarPopup.PlacementTarget = NoteRichTextBox;
                FormatToolbarPopup.HorizontalOffset = midpointX - 60;
                FormatToolbarPopup.VerticalOffset = selectionTop - 35;
                FormatToolbarPopup.IsOpen = true;
            }
        }
        private void FormatBold_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleBold.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }
        private void FormatItalic_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleItalic.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }
        private void FormatUnderline_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleUnderline.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }
        private void FormatStrikethrough_Click(object sender, RoutedEventArgs e)
        {
            var range = NoteRichTextBox.Selection;
            var currentDecoration = range.GetPropertyValue(Inline.TextDecorationsProperty);
            if (currentDecoration == TextDecorations.Strikethrough)
            {
                range.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            }
            else
            {
                range.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Strikethrough);
            }
            NoteRichTextBox.Focus();
        }
        private void FormatHighlight_Click(object sender, RoutedEventArgs e)
        {
            var range = NoteRichTextBox.Selection;
            var currentBackground = range.GetPropertyValue(TextElement.BackgroundProperty);

            var yellowBrush = new SolidColorBrush(Color.FromArgb(80, 255, 235, 59));
            if (currentBackground is SolidColorBrush brush && brush.Color == yellowBrush.Color)
            {
                range.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            }
            else
            {
                range.ApplyPropertyValue(TextElement.BackgroundProperty, yellowBrush);
            }
            NoteRichTextBox.Focus();
        }
        private static readonly Regex UrlRegex = new Regex(
            @"^(https?://[^\s]+|www\.[^\s]+\.[^\s]+)$", RegexOptions.IgnoreCase);
        private bool _isAutoFormatting;

        private void FormatHeading1_Click(object sender, RoutedEventArgs e) => ToggleHeading(18.0);

        private void FormatHeading2_Click(object sender, RoutedEventArgs e) => ToggleHeading(15.0);
        private void ToggleHeading(double targetSize)
        {
            var paragraph = NoteRichTextBox.Selection.Start.Paragraph ?? NoteRichTextBox.CaretPosition.Paragraph;
            if (paragraph == null) return;

            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            bool isThisHeading = range.GetPropertyValue(TextElement.FontSizeProperty) is double size && Math.Abs(size - targetSize) < 0.5;

            if (isThisHeading)
            {
                range.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
                range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            }
            else
            {
                range.ApplyPropertyValue(TextElement.FontSizeProperty, targetSize);
                range.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Bold);
            }
            NoteRichTextBox.Focus();
        }
        private void FormatTextColor_Click(object sender, RoutedEventArgs e)
        {
            var colors = new (string Name, Color Color)[]
            {
                ("White",  Colors.White),
                ("Red",    Color.FromRgb(0xff, 0x6b, 0x6b)),
                ("Orange", Color.FromRgb(0xff, 0xa5, 0x4d)),
                ("Yellow", Color.FromRgb(0xff, 0xd7, 0x00)),
                ("Green",  Color.FromRgb(0x6b, 0xff, 0x8f)),
                ("Blue",   Color.FromRgb(0x4d, 0xb8, 0xff)),
                ("Purple", Color.FromRgb(0xc9, 0x8b, 0xff)),
            };

            var menu = new ContextMenu();
            foreach (var (name, color) in colors)
            {
                var swatch = new Border
                {
                    Width = 13,
                    Height = 13,
                    Background = new SolidColorBrush(color),
                    CornerRadius = new CornerRadius(3),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                var stack = new StackPanel { Orientation = Orientation.Horizontal };
                stack.Children.Add(swatch);
                stack.Children.Add(new TextBlock { Text = name, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center });

                var item = new MenuItem { Header = stack };
                var capturedColor = color;
                item.Click += (s, args) =>
                {
                    if (!NoteRichTextBox.Selection.IsEmpty)
                        NoteRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(capturedColor));
                    NoteRichTextBox.Focus();
                };
                menu.Items.Add(item);
            }

            menu.PlacementTarget = sender as UIElement;
            menu.IsOpen = true;
        }

        private void FormatIncreaseFontSize_Click(object sender, RoutedEventArgs e) => AdjustFontSize(2);

        private void FormatDecreaseFontSize_Click(object sender, RoutedEventArgs e) => AdjustFontSize(-2);
        private void AdjustFontSize(double delta)
        {
            if (NoteRichTextBox.Selection.IsEmpty) return;

            double current = NoteRichTextBox.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size ? size : 14.0;
            double next = Math.Max(8.0, Math.Min(48.0, current + delta));
            NoteRichTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, next);
            NoteRichTextBox.Focus();
        }
        private void FormatBlockquote_Click(object sender, RoutedEventArgs e)
        {
            var paragraph = NoteRichTextBox.Selection.Start.Paragraph ?? NoteRichTextBox.CaretPosition.Paragraph;
            if (paragraph == null) return;

            var range = new TextRange(paragraph.ContentStart, paragraph.ContentEnd);
            bool isQuote = paragraph.BorderThickness.Left > 0;

            if (isQuote)
            {
                paragraph.BorderThickness = new Thickness(0);
                paragraph.Padding = new Thickness(0);
                paragraph.FontStyle = FontStyles.Normal;
                range.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White);
            }
            else
            {
                paragraph.BorderThickness = new Thickness(3, 0, 0, 0);
                paragraph.BorderBrush = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));
                paragraph.Padding = new Thickness(10, 2, 0, 2);
                paragraph.Margin = new Thickness(0, 4, 0, 4);
                paragraph.FontStyle = FontStyles.Italic;
                range.ApplyPropertyValue(TextElement.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)));
            }
            NoteRichTextBox.Focus();
        }
        private void FormatNumberedList_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleNumbering.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }
        private void FormatIndent_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.IncreaseIndentation.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }
        private void FormatOutdent_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.DecreaseIndentation.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }
        private void InsertDivider_Click(object sender, RoutedEventArgs e)
        {
            var divider = new Paragraph(new Run(""))
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x44, 0x44, 0x44)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Margin = new Thickness(0, 10, 0, 10)
            };

            var caretParagraph = NoteRichTextBox.CaretPosition.Paragraph;
            if (caretParagraph != null)
                NoteRichTextBox.Document.Blocks.InsertAfter(caretParagraph, divider);
            else
                NoteRichTextBox.Document.Blocks.Add(divider);

            var following = new Paragraph(new Run(""));
            NoteRichTextBox.Document.Blocks.InsertAfter(divider, following);
            NoteRichTextBox.CaretPosition = following.ContentStart;

            NoteRichTextBox.Focus();
        }
        private void FormatClear_Click(object sender, RoutedEventArgs e)
        {
            if (NoteRichTextBox.Selection.IsEmpty) return;

            var selection = NoteRichTextBox.Selection;
            selection.ApplyPropertyValue(TextElement.FontWeightProperty, FontWeights.Normal);
            selection.ApplyPropertyValue(TextElement.FontStyleProperty, FontStyles.Normal);
            selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            selection.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0);
            selection.ApplyPropertyValue(TextElement.FontFamilyProperty, new FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"));
            selection.ApplyPropertyValue(TextElement.ForegroundProperty, Brushes.White);
            selection.ApplyPropertyValue(TextElement.BackgroundProperty, Brushes.Transparent);
            NoteRichTextBox.Focus();
        }
        private void FormatCodeBlock_Click(object sender, RoutedEventArgs e)
        {
            var codeFont = new FontFamily("Consolas");
            var codeBackground = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x1a));
            var codeForeground = new SolidColorBrush(Color.FromRgb(0xa8, 0xff, 0x78));

            if (!NoteRichTextBox.Selection.IsEmpty)
            {
                var selection = NoteRichTextBox.Selection;
                selection.ApplyPropertyValue(TextElement.FontFamilyProperty, codeFont);
                selection.ApplyPropertyValue(TextElement.BackgroundProperty, codeBackground);
                selection.ApplyPropertyValue(TextElement.ForegroundProperty, codeForeground);
            }
            else
            {
                var paragraph = new Paragraph(new Run(""))
                {
                    FontFamily = codeFont,
                    Background = codeBackground,
                    Foreground = codeForeground,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x3a, 0x3a, 0x3a)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6),
                    Margin = new Thickness(0, 4, 0, 4)
                };

                var caretParagraph = NoteRichTextBox.CaretPosition.Paragraph;
                if (caretParagraph != null)
                    NoteRichTextBox.Document.Blocks.InsertAfter(caretParagraph, paragraph);
                else
                    NoteRichTextBox.Document.Blocks.Add(paragraph);

                NoteRichTextBox.CaretPosition = paragraph.ContentStart;
            }
            NoteRichTextBox.Focus();
        }
        private void FormatBulletList_Click(object sender, RoutedEventArgs e)
        {
            EditingCommands.ToggleBullets.Execute(null, NoteRichTextBox);
            NoteRichTextBox.Focus();
        }
        private const string UncheckedGlyph = "☐ ";
        private const string CheckedGlyph = "☑ ";
        private void InsertCheckbox_Click(object sender, RoutedEventArgs e)
        {
            var caretParagraph = NoteRichTextBox.CaretPosition.Paragraph;
            var newParagraph = new Paragraph();
            var run = new Run(UncheckedGlyph + "New task");
            newParagraph.Inlines.Add(run);

            if (caretParagraph != null)
                NoteRichTextBox.Document.Blocks.InsertAfter(caretParagraph, newParagraph);
            else
                NoteRichTextBox.Document.Blocks.Add(newParagraph);

            var textStart = run.ContentStart.GetPositionAtOffset(UncheckedGlyph.Length) ?? run.ContentStart;
            NoteRichTextBox.Selection.Select(textStart, run.ContentEnd);
            NoteRichTextBox.Focus();
        }
        private static Run? GetChecklistRun(Paragraph? paragraph)
        {
            if (paragraph?.Inlines.FirstInline is Run run &&
                (run.Text.StartsWith(UncheckedGlyph, StringComparison.Ordinal) || run.Text.StartsWith(CheckedGlyph, StringComparison.Ordinal)))
                return run;
            return null;
        }
        private static Run? GetChecklistRunAt(TextPointer? position)
        {
            if (position == null) return null;
            var run = position.Parent as Run ?? position.GetAdjacentElement(LogicalDirection.Forward) as Run;
            if (run == null) return null;

            var checklistRun = GetChecklistRun(run.Parent as Paragraph);
            if (checklistRun != run) return null;

            var glyphEnd = run.ContentStart.GetPositionAtOffset(UncheckedGlyph.Length);
            if (glyphEnd == null || position.CompareTo(glyphEnd) > 0) return null;

            return checklistRun;
        }
        private static void ToggleChecklistItem(Run checklistRun)
        {
            bool wasChecked = checklistRun.Text.StartsWith(CheckedGlyph, StringComparison.Ordinal);
            string rest = checklistRun.Text.Substring(UncheckedGlyph.Length);
            checklistRun.Text = (wasChecked ? UncheckedGlyph : CheckedGlyph) + rest;

            var decoration = wasChecked ? null : TextDecorations.Strikethrough;
            var foreground = wasChecked ? Brushes.White : new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
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
    }
}
