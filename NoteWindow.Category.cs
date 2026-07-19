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
        public void UpdateCategory(string newCategory)
        {
            _note.Category = newCategory;
            DatabaseHelper.UpdateNote(_note);
            NotifyNotesChanged();
            RefreshCategoryDropdown();
        }
        private const string AddNewCategoryOption = "+ New Category...";
        private bool _isRefreshingCategoryCombo;
        private void RefreshCategoryDropdown()
        {
            _isRefreshingCategoryCombo = true;
            try
            {
                string currentCategory = _note.Category ?? "General";
                var categories = DatabaseHelper.ListNotes(null, null)
                    .Select(n => n.Category ?? "General")
                    .Distinct()
                    .OrderBy(c => c)
                    .ToList();
                if (!categories.Contains(currentCategory)) categories.Add(currentCategory);
                categories.Add(AddNewCategoryOption);

                CategoryComboBox.ItemsSource = categories;
                CategoryComboBox.SelectedItem = currentCategory;
            }
            finally
            {
                _isRefreshingCategoryCombo = false;
            }
        }
        private void CategoryComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isRefreshingCategoryCombo) return;
            if (CategoryComboBox.SelectedItem is not string selected) return;

            if (selected == AddNewCategoryOption)
            {
                var dialog = new InputDialog("Enter a new category name:", "New Category", _note.Category ?? "General") { Owner = this };
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Answer))
                {
                    UpdateCategory(dialog.Answer.Trim());
                }
                else
                {
                    RefreshCategoryDropdown();
                }
                return;
            }

            if (selected != (_note.Category ?? "General"))
            {
                UpdateCategory(selected);
            }
        }
        private void DeleteNote()
        {
            var res = MessageBox.Show("Are you sure you want to delete this note?", "Delete Note", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (res == MessageBoxResult.Yes)
            {
                if (!string.IsNullOrEmpty(_note.ImagePath) && File.Exists(_note.ImagePath))
                {
                    try { File.Delete(_note.ImagePath); } catch {}
                }

                DatabaseHelper.DeleteNote(_noteId);
                NotifyNotesChanged();
                this.Close();
            }
        }
    }
}
