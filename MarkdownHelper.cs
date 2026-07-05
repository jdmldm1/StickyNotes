using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace StickyNotes__
{
    public static class MarkdownHelper
    {
        public static FlowDocument Parse(string markdown, Action<int, bool> onTaskChanged)
        {
            var doc = new FlowDocument
            {
                PagePadding = new Thickness(10),
                FontFamily = new FontFamily("Segoe UI, Segoe UI Variable Text, sans-serif"),
                FontSize = 13,
                Foreground = Brushes.White
            };

            string[] lines = markdown.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();
                int lineIndex = i; // Capture index for the event closure

                // 1. Headers
                if (trimmed.StartsWith("# "))
                {
                    var p = new Paragraph { Margin = new Thickness(0, 10, 0, 5) };
                    var run = new Run(trimmed.Substring(2)) { FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 132, 255)) };
                    p.Inlines.Add(run);
                    doc.Blocks.Add(p);
                }
                else if (trimmed.StartsWith("## "))
                {
                    var p = new Paragraph { Margin = new Thickness(0, 8, 0, 4) };
                    var run = new Run(trimmed.Substring(3)) { FontSize = 15, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
                    p.Inlines.Add(run);
                    doc.Blocks.Add(p);
                }
                else if (trimmed.StartsWith("### "))
                {
                    var p = new Paragraph { Margin = new Thickness(0, 6, 0, 3) };
                    var run = new Run(trimmed.Substring(4)) { FontSize = 13.5, FontWeight = FontWeights.Bold, Foreground = Brushes.White };
                    p.Inlines.Add(run);
                    doc.Blocks.Add(p);
                }
                // 2. Checkboxes / Task lists: - [ ] or - [x]
                else if (trimmed.StartsWith("- [ ] ") || trimmed.StartsWith("- [x] ") ||
                         trimmed.StartsWith("* [ ] ") || trimmed.StartsWith("* [x] "))
                {
                    bool isChecked = trimmed.Contains("[x]");
                    string contentText = trimmed.Substring(6);

                    var p = new Paragraph { Margin = new Thickness(10, 2, 0, 2) };
                    
                    // Checkbox Control
                    var cb = new CheckBox
                    {
                        IsChecked = isChecked,
                        Margin = new Thickness(0, 0, 8, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        Cursor = Cursors.Hand
                    };

                    cb.Checked += (s, e) => onTaskChanged(lineIndex, true);
                    cb.Unchecked += (s, e) => onTaskChanged(lineIndex, false);

                    p.Inlines.Add(new InlineUIContainer(cb));
                    
                    // Inline Text
                    var textRun = new Run(contentText);
                    if (isChecked)
                    {
                        textRun.TextDecorations = TextDecorations.Strikethrough;
                        textRun.Foreground = new SolidColorBrush(Color.FromRgb(120, 120, 120));
                    }
                    p.Inlines.Add(textRun);
                    doc.Blocks.Add(p);
                }
                // 3. Bullet list: - or *
                else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    var p = new Paragraph { Margin = new Thickness(15, 2, 0, 2) };
                    p.Inlines.Add(new Run("•  ") { Foreground = new SolidColorBrush(Color.FromRgb(0, 132, 255)), FontWeight = FontWeights.Bold });
                    p.Inlines.Add(new Run(trimmed.Substring(2)));
                    doc.Blocks.Add(p);
                }
                // 4. Code Blocks
                else if (trimmed.StartsWith("```"))
                {
                    var codeBuilder = new System.Text.StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    {
                        codeBuilder.AppendLine(lines[i]);
                        i++;
                    }

                    var border = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(26, 26, 26)),
                        BorderBrush = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(10),
                        Margin = new Thickness(0, 6, 0, 6)
                    };

                    var tb = new TextBox
                    {
                        Text = codeBuilder.ToString().TrimEnd(),
                        Background = Brushes.Transparent,
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 220, 220)),
                        BorderThickness = new Thickness(0),
                        FontFamily = new FontFamily("Consolas, Courier New, monospace"),
                        FontSize = 12,
                        IsReadOnly = true,
                        TextWrapping = TextWrapping.Wrap
                    };

                    border.Child = tb;
                    
                    var p = new Paragraph();
                    p.Inlines.Add(new InlineUIContainer(border));
                    doc.Blocks.Add(p);
                }
                // 5. General text block
                else
                {
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        doc.Blocks.Add(new Paragraph { Margin = new Thickness(0, 0, 0, 6) });
                    }
                    else
                    {
                        var p = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                        p.Inlines.Add(new Run(line));
                        doc.Blocks.Add(p);
                    }
                }
            }

            return doc;
        }
    }
}
