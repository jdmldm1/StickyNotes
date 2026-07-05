using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace StickyNotes__
{
    public partial class SpotlightWindow : Window
    {
        private readonly MainWindow _mainWnd;

        public SpotlightWindow(MainWindow mainWnd)
        {
            InitializeComponent();
            _mainWnd = mainWnd;
        }

        public void FocusSearch()
        {
            SearchInput.Text = "";
            ResultsListBox.ItemsSource = null;
            SearchInput.Focus();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            FocusSearch();
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void SearchInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string query = SearchInput.Text.Trim();
            if (string.IsNullOrEmpty(query))
            {
                ResultsListBox.ItemsSource = null;
                return;
            }

            var notes = DatabaseHelper.ListNotes(query, null);
            var viewModels = notes.Select(n => new NoteCardViewModel
            {
                Id = n.Id,
                Title = n.Title,
                Color = n.Color,
                Snippet = GetPlainTextFromXaml(n.Content),
                ImagePath = n.ImagePath
            }).ToList();

            ResultsListBox.ItemsSource = viewModels;
            if (viewModels.Count > 0)
            {
                ResultsListBox.SelectedIndex = 0;
            }
        }

        private string GetPlainTextFromXaml(string xaml) => NoteContentHelper.ExtractPlainText(xaml);

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                this.Hide();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter)
            {
                OpenSelected();
                e.Handled = true;
            }
            else if (e.Key == Key.Down)
            {
                int nextIndex = ResultsListBox.SelectedIndex + 1;
                if (nextIndex < ResultsListBox.Items.Count)
                {
                    ResultsListBox.SelectedIndex = nextIndex;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }
                e.Handled = true;
            }
            else if (e.Key == Key.Up)
            {
                int prevIndex = ResultsListBox.SelectedIndex - 1;
                if (prevIndex >= 0)
                {
                    ResultsListBox.SelectedIndex = prevIndex;
                    ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
                }
                e.Handled = true;
            }
        }

        private void OpenSelected()
        {
            if (ResultsListBox.SelectedItem is NoteCardViewModel vm)
            {
                this.Hide();
                _mainWnd.OpenNoteWindow(vm.Id);
            }
        }

        private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            OpenSelected();
        }
    }
}
