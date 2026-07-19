using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace StickyNotes__
{
    public partial class MainWindow : Window
    {
        private bool _showOnlyStale;
        private bool _staleNotesDismissedThisSession;
        private static readonly TimeSpan StaleNoteAge = TimeSpan.FromDays(3);
        private static bool IsStaleNote(Note note)
        {
            if (DateTime.Now - note.UpdatedAt < StaleNoteAge) return false;
            return NoteContentHelper.ExtractPlainText(note.Content).Contains('☐');
        }
        private void CheckStaleNotes()
        {
            if (_staleNotesDismissedThisSession)
            {
                StaleNotesBanner.Visibility = Visibility.Collapsed;
                return;
            }

            int staleCount = DatabaseHelper.ListNotes(null, null).Count(IsStaleNote);
            if (staleCount > 0)
            {
                StaleNotesText.Text = $"⏰ {staleCount} note{(staleCount == 1 ? "" : "s")} {(staleCount == 1 ? "has" : "have")} open tasks untouched for {StaleNoteAge.Days}+ days";
                StaleNotesBanner.Visibility = Visibility.Visible;
            }
            else
            {
                StaleNotesBanner.Visibility = Visibility.Collapsed;
            }
        }
        private void StaleNotesBanner_Click(object sender, MouseButtonEventArgs e)
        {
            _showOnlyStale = true;
            RefreshNotesList();
        }
        private void DismissStaleNotesBanner_Click(object sender, RoutedEventArgs e)
        {
            _staleNotesDismissedThisSession = true;
            StaleNotesBanner.Visibility = Visibility.Collapsed;
        }
    }
}
