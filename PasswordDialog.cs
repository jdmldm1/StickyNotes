using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace StickyNotes__
{
    // Masked password entry. In "confirm" mode (used when setting up or changing the vault
    // password) a second field is shown and both must match before OK is accepted.
    public class PasswordDialog : Window
    {
        private readonly PasswordBox _passwordBox;
        private readonly PasswordBox? _confirmBox;
        private readonly TextBlock _errorText;
        public string Password { get; private set; } = "";

        public PasswordDialog(string question, string title, bool confirm = false)
        {
            Title = title;
            Width = 340;
            Height = confirm ? 230 : 175;
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
            if (confirm) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

            _passwordBox = new PasswordBox
            {
                Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                Padding = new Thickness(5, 3, 5, 3),
                Margin = new Thickness(0, 0, 0, confirm ? 8 : 15)
            };
            Grid.SetRow(_passwordBox, 1);
            grid.Children.Add(_passwordBox);

            int nextRow = 2;
            if (confirm)
            {
                _confirmBox = new PasswordBox
                {
                    Background = new SolidColorBrush(Color.FromRgb(45, 45, 45)),
                    Foreground = Brushes.White,
                    CaretBrush = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(68, 68, 68)),
                    Padding = new Thickness(5, 3, 5, 3),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                Grid.SetRow(_confirmBox, nextRow);
                grid.Children.Add(_confirmBox);
                nextRow++;
            }

            _errorText = new TextBlock
            {
                Text = "",
                Foreground = new SolidColorBrush(Color.FromRgb(255, 107, 107)),
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_errorText, nextRow);
            grid.Children.Add(_errorText);
            nextRow++;

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
            okButton.Click += (s, e) => TryAccept();

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

            Grid.SetRow(buttonPanel, nextRow);
            grid.Children.Add(buttonPanel);

            border.Child = grid;
            Content = border;

            Loaded += (s, e) => _passwordBox.Focus();
            MouseDown += (s, e) => { if (e.ChangedButton == MouseButton.Left) DragMove(); };

            KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) TryAccept();
                else if (e.Key == Key.Escape) { DialogResult = false; Close(); }
            };
        }

        private void TryAccept()
        {
            string pwd = _passwordBox.Password;
            if (string.IsNullOrEmpty(pwd))
            {
                ShowError("Enter a password.");
                return;
            }
            if (_confirmBox != null && pwd != _confirmBox.Password)
            {
                ShowError("Passwords don't match.");
                return;
            }

            Password = pwd;
            DialogResult = true;
            Close();
        }

        private void ShowError(string message)
        {
            _errorText.Text = message;
            _errorText.Visibility = Visibility.Visible;
        }
    }
}
