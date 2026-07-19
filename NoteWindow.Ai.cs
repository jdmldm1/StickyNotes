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
        private void AiFormatButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();

            var summarizeItem = new MenuItem { Header = "Summarize Note" };
            summarizeItem.Click += async (s, args) => await ApplyAiFormatAsync("Summarize this text in 2-3 short sentences, preserving key details: ");
            menu.Items.Add(summarizeItem);

            var bulletItem = new MenuItem { Header = "Format as Bullet Points" };
            bulletItem.Click += async (s, args) => await ApplyAiFormatAsync("Rewrite this text as a clean bulleted list, preserving all key details: ");
            menu.Items.Add(bulletItem);

            var grammarItem = new MenuItem { Header = "Correct Grammar & Spelling" };
            grammarItem.Click += async (s, args) => await ApplyAiFormatAsync("Fix all spelling, punctuation, and grammatical errors in this text. Do not change style or rewrite unnecessarily, just fix mistakes: ");
            menu.Items.Add(grammarItem);

            var professionalItem = new MenuItem { Header = "Rewrite Professionally" };
            professionalItem.Click += async (s, args) => await ApplyAiFormatAsync("Rewrite this text to have a highly professional, polite, and clear business tone: ");
            menu.Items.Add(professionalItem);

            menu.Items.Add(new Separator());

            var actionItemsItem = new MenuItem { Header = "Extract Action Items" };
            actionItemsItem.Click += async (s, args) => await ExtractActionItemsAsync();
            menu.Items.Add(actionItemsItem);

            menu.IsOpen = true;
        }
        private async void AiSummaryButton_Click(object sender, RoutedEventArgs e)
        {
            await InsertTldrSummaryAsync();
        }
        private async System.Threading.Tasks.Task InsertTldrSummaryAsync()
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string plainText = range.Text.Trim();
            if (string.IsNullOrEmpty(plainText)) return;

            var oldCursor = this.Cursor;
            this.Cursor = Cursors.Wait;
            try
            {
                string prompt = "Summarize the following text in 1-2 very short, concise sentences (acting as a TL;DR summary of the main points):\n\n" + plainText;
                string aiOutput = await AiHelper.GenerateTextAsync(prompt);
                if (!string.IsNullOrEmpty(aiOutput))
                {
                    var tldrParagraph = new Paragraph();
                    tldrParagraph.Inlines.Add(new Run("TL;DR: ") { FontWeight = FontWeights.Bold });
                    tldrParagraph.Inlines.Add(new Run(aiOutput) { FontStyle = FontStyles.Italic });
                    tldrParagraph.Margin = new Thickness(0, 0, 0, 6);

                    var divider = new Paragraph(new Run("――――――――――――――――――――"))
                    {
                        Foreground = new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)),
                        Margin = new Thickness(0, 0, 0, 10)
                    };

                    if (NoteRichTextBox.Document.Blocks.FirstBlock != null)
                    {
                        NoteRichTextBox.Document.Blocks.InsertBefore(NoteRichTextBox.Document.Blocks.FirstBlock, tldrParagraph);
                        NoteRichTextBox.Document.Blocks.InsertAfter(tldrParagraph, divider);
                    }
                    else
                    {
                        NoteRichTextBox.Document.Blocks.Add(tldrParagraph);
                        NoteRichTextBox.Document.Blocks.Add(divider);
                    }

                    SaveNoteContent();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI TL;DR generation failed: " + ex.Message, "AI Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                this.Cursor = oldCursor;
            }
        }
        private async System.Threading.Tasks.Task ExtractActionItemsAsync()
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string plainText = range.Text.Trim();
            if (string.IsNullOrEmpty(plainText)) return;

            var oldCursor = this.Cursor;
            this.Cursor = Cursors.Wait;
            try
            {
                string prompt = "Extract any action items, to-dos, or follow-up tasks mentioned in the following text. " +
                    "Respond with ONLY a JSON array of short task strings (no explanations, no markdown, no code fences). " +
                    "If there are no action items, respond with an empty array [].\n\nText:\n" + plainText;

                string aiOutput = await AiHelper.GenerateTextAsync(prompt);
                var tasks = AiHelper.ParseJsonStringArray(aiOutput)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList();

                if (tasks.Count == 0)
                {
                    MessageBox.Show("No action items were found in this note.", "Extract Action Items", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var heading = new Paragraph(new Run("Action Items")) { FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 10, 0, 3) };
                NoteRichTextBox.Document.Blocks.Add(heading);
                foreach (var task in tasks)
                {
                    NoteRichTextBox.Document.Blocks.Add(new Paragraph(new Run(UncheckedGlyph + task.Trim())));
                }

                SaveNoteContent();
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI extraction failed: " + ex.Message, "AI Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                this.Cursor = oldCursor;
            }
        }
        private async System.Threading.Tasks.Task ApplyAiFormatAsync(string promptPrefix)
        {
            TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
            string plainText = range.Text.Trim();
            if (string.IsNullOrEmpty(plainText)) return;

            var oldCursor = this.Cursor;
            this.Cursor = Cursors.Wait;
            try
            {
                string prompt = promptPrefix + "\n\nText:\n" + plainText;
                string aiOutput = await AiHelper.GenerateTextAsync(prompt);
                if (!string.IsNullOrEmpty(aiOutput))
                {
                    NoteRichTextBox.Document.Blocks.Clear();
                    NoteRichTextBox.Document.Blocks.Add(new Paragraph(new Run(aiOutput)));
                    SaveNoteContent();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("AI formatting failed: " + ex.Message, "AI Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                this.Cursor = oldCursor;
            }
        }
        private Border? _typingBubble;
        private bool _isAiChatActive = false;
        private void AiChatToggleButton_Click(object sender, RoutedEventArgs e)
        {
            _isAiChatActive = !_isAiChatActive;

            if (_isAiChatActive)
            {
                AiChatPanel.Visibility = Visibility.Visible;
                AiPromptTextBox.Focus();

                if (ChatHistoryPanel.Children.Count == 0)
                {
                    AddChatBubble("Hello! I am your AI assistant. You can ask me questions about this note or its attached screenshot.", false);
                }
            }
            else
            {
                AiChatPanel.Visibility = Visibility.Collapsed;
            }
        }
        private async void SendAiPrompt_Click(object sender, RoutedEventArgs e)
        {
            await ProcessAiChatQueryAsync();
        }
        private async void AiPromptTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await ProcessAiChatQueryAsync();
            }
        }
        private async System.Threading.Tasks.Task ProcessAiChatQueryAsync()
        {
            string query = AiPromptTextBox.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            AiPromptTextBox.Text = "";
            AddChatBubble(query, true);
            ShowTypingIndicator();

            try
            {
                TextRange range = new TextRange(NoteRichTextBox.Document.ContentStart, NoteRichTextBox.Document.ContentEnd);
                string noteText = range.Text.Trim();

                string prompt = $"You are a helpful desktop note assistant. Answer the user's question contextually based on the note text provided below. If there are screenshots attached, their OCR text is also included. Answer clearly and keep it short.\n\nNote Context:\n{noteText}\n\nUser Question:\n{query}";

                string aiResponse = await AiHelper.GenerateTextAsync(prompt);
                
                RemoveTypingIndicator();
                if (string.IsNullOrEmpty(aiResponse))
                {
                    AddChatBubble("Sorry, I could not generate a response. Please check if Ollama is running.", false);
                }
                else
                {
                    AddChatBubble(aiResponse, false);
                }
            }
            catch (System.Exception ex)
            {
                RemoveTypingIndicator();
                AddChatBubble("Error during processing: " + ex.Message, false);
            }
        }
        private void AddChatBubble(string text, bool isUser)
        {
            var border = new Border
            {
                Background = isUser ? new SolidColorBrush(Color.FromRgb(0, 132, 255)) : new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = isUser ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                MaxWidth = this.ActualWidth * 0.75
            };

            var tb = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            };

            border.Child = tb;
            ChatHistoryPanel.Children.Add(border);
            ChatScrollViewer.ScrollToEnd();
        }
        private void ShowTypingIndicator()
        {
            _typingBubble = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(40, 40, 40)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 6, 10, 6),
                Margin = new Thickness(0, 4, 0, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };

            var tb = new TextBlock
            {
                Text = "AI is thinking...",
                Foreground = new SolidColorBrush(Color.FromRgb(150, 150, 150)),
                FontSize = 11,
                FontStyle = FontStyles.Italic
            };

            _typingBubble.Child = tb;
            ChatHistoryPanel.Children.Add(_typingBubble);
            ChatScrollViewer.ScrollToEnd();
        }
        private void RemoveTypingIndicator()
        {
            if (_typingBubble != null)
            {
                ChatHistoryPanel.Children.Remove(_typingBubble);
                _typingBubble = null;
            }
        }
    }
}
