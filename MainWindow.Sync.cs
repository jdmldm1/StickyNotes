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
        private System.Windows.Threading.DispatcherTimer? _jeffsNotesSyncTimer;
        private bool _jeffsNotesSyncInProgress;
        public void ApplyJeffsNotesSyncSettings()
        {
            var config = SettingsService.Current;

            _jeffsNotesSyncTimer?.Stop();
            _jeffsNotesSyncTimer = null;

            if (!config.JeffsNotesSyncEnabled || string.IsNullOrWhiteSpace(config.JeffsNotesUrl))
                return;

            int minutes = config.JeffsNotesSyncIntervalMinutes > 0 ? config.JeffsNotesSyncIntervalMinutes : 15;
            _jeffsNotesSyncTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(minutes)
            };
            _jeffsNotesSyncTimer.Tick += async (s, e) => await RunBackgroundJeffsNotesSyncAsync();
            _jeffsNotesSyncTimer.Start();

            _ = RunBackgroundJeffsNotesSyncAsync();
        }
        private async Task RunBackgroundJeffsNotesSyncAsync()
        {
            var config = SettingsService.Current;
            if (!config.JeffsNotesSyncEnabled || string.IsNullOrWhiteSpace(config.JeffsNotesUrl)) return;

            try
            {
                await RunJeffsNotesSyncAsync(config.JeffsNotesUrl);
            }
            catch (Exception ex)
            {
                // Background sync failures (e.g. server unreachable from work) are expected and silent.
                Console.WriteLine("Background JeffsNotes sync failed: " + ex.Message);
            }
        }
        public async Task<JeffsNotesSyncResult> RunJeffsNotesSyncAsync(string url)
        {
            if (_jeffsNotesSyncInProgress)
                return new JeffsNotesSyncResult { Success = false, Error = "A sync is already in progress." };

            _jeffsNotesSyncInProgress = true;
            try
            {
                var config = SettingsService.Current;
                var result = await JeffsNotesSyncService.SyncAsync(url, config.JeffsNotesLastSyncedAt);

                if (result.NewWatermark != null)
                {
                    config.JeffsNotesLastSyncedAt = result.NewWatermark;
                    SettingsService.Save(config);
                }

                foreach (int noteId in result.AffectedLocalNoteIds)
                {
                    if (_openNoteWindows.TryGetValue(noteId, out var noteWnd))
                        noteWnd.ReloadNoteFromDb();
                }

                if (result.Pulled > 0 || result.DeletedLocally > 0)
                {
                    RefreshNotesList();
                    RefreshTagsFilter();
                }

                return result;
            }
            finally
            {
                _jeffsNotesSyncInProgress = false;
            }
        }
    }
}
