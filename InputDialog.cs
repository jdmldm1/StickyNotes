using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StickyNotes__
{
    public class InputDialog : Window
    {
        private TextBox _inputTextBox;
        public string Answer { get; private set; } = "";

        public InputDialog(string question, string title, string defaultAnswer = "")
        {
            Title = title;
            Width = 320;
            Height = 150;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;

            var border = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0, 132, 255)),
                BorderThickness = new Thickness(1.5),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(15)
            };

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var questionTextBlock = new TextBlock
            {
                Text = question,
                Foreground = Brushes.White,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 10),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(questionTextBlock, 0);
            grid.Children.Add(questionTextBlock);

            _inputTextBox = new TextBox
            {
                Text = defaultAnswer,
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Padding = new Thickness(5, 3, 5, 3),
                Margin = new Thickness(0, 0, 0, 15)
            };
            Grid.SetRow(_inputTextBox, 1);
            grid.Children.Add(_inputTextBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            
            var okButton = new Button 
            { 
                Content = "OK", 
                Width = 70, 
                Height = 26, 
                Margin = new Thickness(0, 0, 8, 0), 
                Background = new SolidColorBrush(Color.FromRgb(0, 132, 255)), 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(0), 
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
            okButton.Click += (s, e) => { Answer = _inputTextBox.Text.Trim(); DialogResult = true; Close(); };
            
            var cancelButton = new Button 
            { 
                Content = "Cancel", 
                Width = 70, 
                Height = 26, 
                Background = new SolidColorBrush(Color.FromRgb(58, 58, 58)), 
                Foreground = Brushes.White, 
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };
            cancelButton.Click += (s, e) => { DialogResult = false; Close(); };

            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            Content = border;

            Loaded += (s, e) => { _inputTextBox.Focus(); _inputTextBox.SelectAll(); };
            MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    Answer = _inputTextBox.Text.Trim();
                    DialogResult = true;
                    Close();
                }
                else if (e.Key == Key.Escape)
                {
                    DialogResult = false;
                    Close();
                }
            };
        }
    }
}
