using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace StickyNotes__
{
    public class ExportBlock
    {
        public string Text { get; set; } = "";
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strikethrough { get; set; }
        public bool IsHeading { get; set; }
        public bool IsBullet { get; set; }
        public bool IsCode { get; set; }
        public string? HyperlinkUrl { get; set; }
    }

    public static class NoteExportHelper
    {
        public static List<ExportBlock> ExtractExportBlocks(FlowDocument document)
        {
            var results = new List<ExportBlock>();
            CollectBlocks(document.Blocks, results, false);
            return results;
        }

        private static void CollectBlocks(BlockCollection blocks, List<ExportBlock> results, bool isBullet)
        {
            foreach (var block in blocks)
            {
                switch (block)
                {
                    case Paragraph paragraph:
                        results.Add(BuildExportBlock(paragraph, isBullet));
                        break;
                    case System.Windows.Documents.List list:
                        foreach (var item in list.ListItems)
                            CollectBlocks(item.Blocks, results, true);
                        break;
                    case Section section:
                        CollectBlocks(section.Blocks, results, isBullet);
                        break;
                }
            }
        }

        private static ExportBlock BuildExportBlock(Paragraph paragraph, bool isBullet)
        {
            var sb = new StringBuilder();
            string? hyperlinkUrl = null;
            bool bold = paragraph.FontWeight == System.Windows.FontWeights.Bold;
            bool italic = paragraph.FontStyle == System.Windows.FontStyles.Italic;
            bool isHeading = paragraph.FontSize >= 16;
            bool isCode = paragraph.FontFamily?.Source == "Consolas";
            bool underline = false;
            bool strikethrough = false;

            AppendInlines(paragraph.Inlines, sb, ref hyperlinkUrl, ref bold, ref italic, ref underline, ref strikethrough);

            return new ExportBlock
            {
                Text = sb.ToString(),
                Bold = bold,
                Italic = italic,
                Underline = underline,
                Strikethrough = strikethrough,
                IsHeading = isHeading,
                IsBullet = isBullet,
                IsCode = isCode,
                HyperlinkUrl = hyperlinkUrl
            };
        }

        private static void AppendInlines(InlineCollection inlines, StringBuilder sb, ref string? hyperlinkUrl, ref bool bold, ref bool italic, ref bool underline, ref bool strikethrough)
        {
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case Hyperlink link:
                        hyperlinkUrl ??= link.NavigateUri?.AbsoluteUri;
                        AppendInlines(link.Inlines, sb, ref hyperlinkUrl, ref bold, ref italic, ref underline, ref strikethrough);
                        break;
                    case Span span:
                        if (span.FontWeight == System.Windows.FontWeights.Bold) bold = true;
                        if (span.FontStyle == System.Windows.FontStyles.Italic) italic = true;
                        ApplyDecorations(span.TextDecorations, ref underline, ref strikethrough);
                        AppendInlines(span.Inlines, sb, ref hyperlinkUrl, ref bold, ref italic, ref underline, ref strikethrough);
                        break;
                    case Run run:
                        if (run.FontWeight == System.Windows.FontWeights.Bold) bold = true;
                        if (run.FontStyle == System.Windows.FontStyles.Italic) italic = true;
                        ApplyDecorations(run.TextDecorations, ref underline, ref strikethrough);
                        sb.Append(run.Text);
                        break;
                    case LineBreak:
                        sb.Append('\n');
                        break;
                }
            }
        }

        private static void ApplyDecorations(TextDecorationCollection? decorations, ref bool underline, ref bool strikethrough)
        {
            if (decorations == null) return;
            foreach (var decoration in decorations)
            {
                if (decoration.Location == TextDecorationLocation.Underline) underline = true;
                if (decoration.Location == TextDecorationLocation.Strikethrough) strikethrough = true;
            }
        }

        public static void WriteDocx(string path, string title, List<ExportBlock> blocks)
        {
            using var document = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(
                path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);

            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new W.Document();
            var body = mainPart.Document.AppendChild(new W.Body());

            var titleRun = new W.Run(
                new W.RunProperties(new W.Bold(), new W.FontSize { Val = "36" }),
                new W.Text(title) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
            body.AppendChild(new W.Paragraph(titleRun));

            foreach (var block in blocks)
            {
                if (string.IsNullOrEmpty(block.Text))
                {
                    body.AppendChild(new W.Paragraph());
                    continue;
                }

                string text = block.IsBullet ? "•  " + block.Text : block.Text;
                var para = new W.Paragraph();

                if (block.HyperlinkUrl != null)
                {
                    var rel = mainPart.AddHyperlinkRelationship(new Uri(block.HyperlinkUrl), true);
                    var linkRun = new W.Run(
                        new W.RunProperties(new W.Underline { Val = W.UnderlineValues.Single }, new W.Color { Val = "0563C1" }),
                        new W.Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
                    para.AppendChild(new W.Hyperlink(linkRun) { Id = rel.Id });
                }
                else
                {
                    var runProps = new W.RunProperties();
                    if (block.Bold || block.IsHeading) runProps.Append(new W.Bold());
                    if (block.Italic) runProps.Append(new W.Italic());
                    if (block.Underline) runProps.Append(new W.Underline { Val = W.UnderlineValues.Single });
                    if (block.Strikethrough) runProps.Append(new W.Strike());
                    if (block.IsHeading) runProps.Append(new W.FontSize { Val = "28" });
                    if (block.IsCode) runProps.Append(new W.RunFonts { Ascii = "Consolas" });

                    var run = new W.Run(runProps, new W.Text(text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve });
                    para.AppendChild(run);
                }

                body.AppendChild(para);
            }
        }

        public static void WritePdf(string path, string title, List<ExportBlock> blocks)
        {
            QuestPDF.Fluent.Document.Create(container =>
            {
                container.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(40);
                    page.DefaultTextStyle(x => x.FontSize(11));

                    page.Content().Column(col =>
                    {
                        col.Item().Text(title).Bold().FontSize(20);
                        col.Item().PaddingBottom(10);

                        foreach (var block in blocks)
                        {
                            if (string.IsNullOrEmpty(block.Text))
                            {
                                col.Item().PaddingBottom(6);
                                continue;
                            }

                            col.Item().Row(row =>
                            {
                                if (block.IsBullet)
                                {
                                    row.ConstantItem(15).Text("•");
                                }

                                var textContainer = block.HyperlinkUrl != null
                                    ? row.RelativeItem().Hyperlink(block.HyperlinkUrl)
                                    : row.RelativeItem();

                                textContainer.Text(text =>
                                {
                                    var span = text.Span(block.Text);
                                    if (block.Bold || block.IsHeading) span.Bold();
                                    if (block.Italic) span.Italic();
                                    if (block.Underline || block.HyperlinkUrl != null) span.Underline();
                                    if (block.Strikethrough) span.Strikethrough();
                                    if (block.IsHeading) span.FontSize(14);
                                    if (block.IsCode) span.FontFamily("Consolas");
                                    if (block.HyperlinkUrl != null) span.FontColor(QuestPDF.Helpers.Colors.Blue.Medium);
                                });
                            });
                        }
                    });
                });
            }).GeneratePdf(path);
        }
    }
}
