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
        private void ApplyColor()
        {
            string colorName = _note.Color;
            if (!ColorProfiles.ContainsKey(colorName))
                colorName = "yellow";

            var profile = ColorProfiles[colorName];

            WindowBorder.Background = profile.DarkBg;
            WindowBorder.BorderBrush = profile.DarkHeader;
            HeaderGrid.Background = profile.DarkHeader;
            
            TitleTextBlock.Foreground = profile.DarkText;
            NoteTitleTextBox.Foreground = profile.DarkText;
            NoteTitleTextBox.CaretBrush = profile.DarkText;
            NoteRichTextBox.Foreground = profile.DarkText;

            if (profile.DarkText is SolidColorBrush scb)
            {
                TitleDivider.Background = new SolidColorBrush(Color.FromArgb(50, scb.Color.R, scb.Color.G, scb.Color.B));
            }
            else
            {
                TitleDivider.Background = new SolidColorBrush(Color.FromArgb(50, 255, 255, 255));
            }

            var wndHelper = new WindowInteropHelper(this);
            Win32Helper.EnableMica(wndHelper.Handle, true);
        }
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                this.DragMove();
            }
        }
        private void NewButton_Click(object sender, RoutedEventArgs e)
        {
            var parent = Application.Current.MainWindow as MainWindow;
            parent?.CreateNewNote();
        }
        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
        private void OptionsButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu();
            menu.Style = (Style)FindResource(typeof(ContextMenu));

            var colorsItem = new MenuItem { Header = "Change Color" };
            foreach (var profile in ColorProfiles)
            {
                var item = new MenuItem { Header = profile.Value.Name };
                string colorKey = profile.Key;
                item.Click += (s, args) => ChangeColor(colorKey);
                colorsItem.Items.Add(item);
            }
            menu.Items.Add(colorsItem);

            var categoryItem = new MenuItem { Header = "Move to Category" };
            string currentCat = _note.Category ?? "General";
            var allNotes = DatabaseHelper.ListNotes(null, null);
            var existingCats = allNotes
                .Select(n => n.Category ?? "General")
                .Distinct()
                .OrderBy(c => c)
                .ToList();
            foreach (var cat in existingCats)
            {
                var subItem = new MenuItem { Header = cat, IsCheckable = true, IsChecked = cat == currentCat };
                string catCopy = cat;
                subItem.Click += (s, args) => UpdateCategory(catCopy);
                categoryItem.Items.Add(subItem);
            }
            var newCatSub = new MenuItem { Header = "+ New Category..." };
            newCatSub.Click += (s, args) =>
            {
                var dialog = new InputDialog("Enter a new category name:", "New Category", currentCat)
                {
                    Owner = this
                };
                if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.Answer))
                    UpdateCategory(dialog.Answer.Trim());
            };
            categoryItem.Items.Add(new Separator());
            categoryItem.Items.Add(newCatSub);
            menu.Items.Add(categoryItem);
            menu.Items.Add(new Separator());

            var copyItem = new MenuItem { Header = "Copy Note Text" };
            copyItem.Click += (s, args) => CopyNoteToClipboard();
            menu.Items.Add(copyItem);

            var exportItem = new MenuItem { Header = "Export Note" };
            var exportDocxItem = new MenuItem { Header = "Export as Word (.docx)" };
            exportDocxItem.Click += (s, args) => ExportNote("docx");
            exportItem.Items.Add(exportDocxItem);
            var exportPdfItem = new MenuItem { Header = "Export as PDF (.pdf)" };
            exportPdfItem.Click += (s, args) => ExportNote("pdf");
            exportItem.Items.Add(exportPdfItem);
            menu.Items.Add(exportItem);
            menu.Items.Add(new Separator());

            var saveAsTemplateItem = new MenuItem { Header = "Save as Template" };
            saveAsTemplateItem.Click += (s, args) =>
            {
                SaveNoteContent();
                DatabaseHelper.SetNoteIsTemplate(_noteId, true);
                
                var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
                if (main != null)
                {
                    main.ShowStatusToast("Saved as template ✅");
                }
            };
            menu.Items.Add(saveAsTemplateItem);
            menu.Items.Add(new Separator());

            var secureItem = new MenuItem { Header = _note.IsSecure ? "🔓 Remove Security" : "🔒 Make Secure Note" };
            secureItem.Click += (s, args) => ToggleSecureNote();
            menu.Items.Add(secureItem);
            menu.Items.Add(new Separator());

            var deleteItem = new MenuItem { Header = "Delete Note" };
            deleteItem.Click += (s, args) => DeleteNote();
            menu.Items.Add(deleteItem);

            menu.IsOpen = true;
        }
        private void ToggleSecureNote()
        {
            var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;

            if (_note.IsSecure)
            {
                // Removing security: decrypt back to plain content permanently.
                if (!VaultService.IsUnlocked)
                {
                    var unlockDialog = new PasswordDialog("Enter your vault password to remove security from this note.", "Unlock Vault") { Owner = this };
                    if (unlockDialog.ShowDialog() != true) return;
                    if (!VaultService.TryUnlock(unlockDialog.Password))
                    {
                        MessageBox.Show("Incorrect password.", "Unlock Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                string plainXaml;
                try
                {
                    plainXaml = string.IsNullOrEmpty(_note.Content) ? "" : VaultService.Decrypt(_note.Content);
                }
                catch
                {
                    MessageBox.Show("Couldn't decrypt this note - the content may be corrupted.", "Remove Security Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _note.IsSecure = false;
                _note.Content = plainXaml;
                DatabaseHelper.UpdateNote(_note);

                LoadNoteData();
                main?.RefreshNotesList();
                main?.RefreshTagsFilter();
                main?.ShowStatusToast("Security removed 🔓");
            }
            else
            {
                // Making secure: ensure a vault password exists and is unlocked, then encrypt the
                // note's current content and strip any plain_text/ocr_text (handled by UpdateNote).
                if (!VaultService.IsConfigured)
                {
                    var setupDialog = new PasswordDialog(
                        "Set a password for your secure notes vault. This protects all notes you mark as secure.\n\nThere is no way to recover this password if you forget it.",
                        "Set Up Secure Notes", confirm: true) { Owner = this };
                    if (setupDialog.ShowDialog() != true) return;
                    VaultService.SetupVault(setupDialog.Password);
                }
                else if (!VaultService.IsUnlocked)
                {
                    var unlockDialog = new PasswordDialog("Enter your vault password to make this note secure.", "Unlock Vault") { Owner = this };
                    if (unlockDialog.ShowDialog() != true) return;
                    if (!VaultService.TryUnlock(unlockDialog.Password))
                    {
                        MessageBox.Show("Incorrect password.", "Unlock Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                SaveNoteContent(); // flush any pending edits into _note.Content first (still plaintext at this point)
                string plainXaml = _note.Content;

                _note.IsSecure = true;
                _note.Content = VaultService.Encrypt(plainXaml);
                _note.OcrText = null;
                DatabaseHelper.UpdateNote(_note);

                LoadNoteData();
                main?.RefreshNotesList();
                main?.RefreshTagsFilter();
                main?.ShowStatusToast("Note is now secure 🔒");
            }
        }
        public void ChangeColor(string colorKey)
        {
            _note.Color = colorKey;
            DatabaseHelper.UpdateNote(_note);
            ApplyColor();
            NotifyNotesChanged();
        }
        private void Window_LocationChanged(object sender, EventArgs e)
        {
            SaveGeometry();
        }
        private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            SaveGeometry();
        }
        private void SaveGeometry()
        {
            if (_isLoaded)
            {
                _note.X = (int)this.Left;
                _note.Y = (int)this.Top;
                _note.W = (int)this.Width;
                _note.H = (int)this.Height;
                DatabaseHelper.UpdateNote(_note);
            }
        }
    }
}
