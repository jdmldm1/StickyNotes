using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace StickyNotes__
{
    public class AppConfigData
    {
        public string Url { get; set; } = "";
        public string Email { get; set; } = "";
        public string Token { get; set; } = "";
        public string Space { get; set; } = "";
        public string ParentId { get; set; } = "";
        public string OllamaUrl { get; set; } = "http://localhost:11434";
        public string OllamaModel { get; set; } = "";
        public double SidebarOpacity { get; set; } = 0.9;
    }

    public partial class SettingsWindow : Window
    {
        private double _originalOpacity = 0.9;

        public SettingsWindow()
        {
            InitializeComponent();
            LoadSettings();
            DataLocationPathText.Text = AppConfig.AppDir;
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(AppConfig.SettingsPath))
                {
                    string json = File.ReadAllText(AppConfig.SettingsPath);
                    var config = JsonSerializer.Deserialize<AppConfigData>(json);
                    if (config != null)
                    {
                        OllamaUrlTextBox.Text = string.IsNullOrEmpty(config.OllamaUrl) ? "http://localhost:11434" : config.OllamaUrl;
                        OllamaModelTextBox.Text = config.OllamaModel;

                        double opacityPct = Math.Round(config.SidebarOpacity * 100);
                        opacityPct = Math.Max(20, Math.Min(100, opacityPct));
                        _originalOpacity = config.SidebarOpacity;
                        OpacitySlider.Value = opacityPct;
                        OpacityLabel.Text = $"{(int)opacityPct}%";
                    }
                }
                else
                {
                    OllamaUrlTextBox.Text = "http://localhost:11434";
                    OpacitySlider.Value = 90;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error loading settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OpacitySlider_ValueChanged(object sender, System.Windows.RoutedPropertyChangedEventArgs<double> e)
        {
            if (OpacityLabel == null) return;
            int pct = (int)OpacitySlider.Value;
            OpacityLabel.Text = $"{pct}%";
            double opacity = pct / 100.0;
            if (Owner is MainWindow main)
                main.ApplySidebarOpacity(opacity);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                AppConfigData? config = null;
                if (File.Exists(AppConfig.SettingsPath))
                {
                    try
                    {
                        string json = File.ReadAllText(AppConfig.SettingsPath);
                        config = JsonSerializer.Deserialize<AppConfigData>(json) ?? new AppConfigData();
                    }
                    catch {}
                }
                if (config == null) config = new AppConfigData();

                config.OllamaUrl = OllamaUrlTextBox.Text.Trim();
                config.OllamaModel = OllamaModelTextBox.Text.Trim();
                config.SidebarOpacity = OpacitySlider.Value / 100.0;

                string newJson = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AppConfig.SettingsPath, newJson);
                
                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving settings: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ImportStickyNotesButton_Click(object sender, RoutedEventArgs e)
        {
            ImportStickyNotesButton.IsEnabled = false;
            string originalContent = ImportStickyNotesButton.Content.ToString() ?? "Import Windows Sticky Notes";
            ImportStickyNotesButton.Content = "Importing...";

            try
            {
                int count = DatabaseHelper.ImportFromMicrosoftStickyNotes();
                if (count == 0)
                {
                    MessageBox.Show("No active notes found in Windows Sticky Notes to import.", "Import Notes", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show($"Successfully imported {count} notes from Windows Sticky Notes!", "Import Notes", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    if (Owner is MainWindow mainWin)
                    {
                        mainWin.RefreshNotesList();
                    }
                    else if (Application.Current.MainWindow is MainWindow main)
                    {
                        main.RefreshNotesList();
                    }
                }
            }
            catch (FileNotFoundException)
            {
                MessageBox.Show("Windows Sticky Notes database not found on this machine. Ensure the app is installed and has active data.", "Import Notes", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to import notes: {ex.Message}", "Import Notes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ImportStickyNotesButton.IsEnabled = true;
                ImportStickyNotesButton.Content = originalContent;
            }
        }

        private async void AutoOrganizeSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var main = Owner as MainWindow ?? Application.Current.MainWindow as MainWindow;
            if (main == null) return;

            AutoOrganizeSettingsButton.IsEnabled = false;
            string originalContent = AutoOrganizeSettingsButton.Content.ToString() ?? "🪄 Auto Organize Notes (AI)";
            AutoOrganizeSettingsButton.Content = "🪄 Organizing...";

            try
            {
                await main.RunAutoOrganizeAsync();
            }
            finally
            {
                AutoOrganizeSettingsButton.IsEnabled = true;
                AutoOrganizeSettingsButton.Content = originalContent;
            }
        }

        private void BackupNowButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Back Up StickyNotes++",
                Filter = "StickyNotes++ Backup (*.snbackup)|*.snbackup",
                FileName = $"StickyNotes++_Backup_{DateTime.Now:yyyy-MM-dd}.snbackup"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                BackupNowButton.IsEnabled = false;
                BackupHelper.CreateBackup(dialog.FileName);
                BackupStatusTextBlock.Foreground = System.Windows.Media.Brushes.LightGreen;
                BackupStatusTextBlock.Text = $"Backup saved to {dialog.FileName}";
            }
            catch (Exception ex)
            {
                BackupStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                BackupStatusTextBlock.Text = "Backup failed: " + ex.Message;
            }
            finally
            {
                BackupNowButton.IsEnabled = true;
            }
        }

        private void RestoreBackupButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "Restoring will replace all of your current notes, images, and attachments with the contents of the backup. This cannot be undone. Continue?",
                "Restore from Backup", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Restore StickyNotes++ Backup",
                Filter = "StickyNotes++ Backup (*.snbackup)|*.snbackup|All files (*.*)|*.*"
            };

            if (dialog.ShowDialog() != true) return;

            try
            {
                RestoreBackupButton.IsEnabled = false;
                BackupHelper.RestoreBackup(dialog.FileName);
                BackupStatusTextBlock.Foreground = System.Windows.Media.Brushes.LightGreen;
                BackupStatusTextBlock.Text = "Backup restored successfully.";

                if (Owner is MainWindow mainWin)
                {
                    mainWin.RefreshNotesList();
                    mainWin.RefreshTagsFilter();
                }
                else if (Application.Current.MainWindow is MainWindow main)
                {
                    main.RefreshNotesList();
                    main.RefreshTagsFilter();
                }
            }
            catch (Exception ex)
            {
                BackupStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                BackupStatusTextBlock.Text = "Restore failed: " + ex.Message;
            }
            finally
            {
                RestoreBackupButton.IsEnabled = true;
            }
        }

        private void ChangeDataLocationButton_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "Choose a folder where StickyNotes++ will store its data",
                UseDescriptionForTitle = true
            };

            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            string newDataDir = Path.Combine(dialog.SelectedPath, "StickyNotesPlus");

            var confirm = MessageBox.Show(
                $"Move your notes, images, and attachments to:\n{newDataDir}\n\nThe app will restart afterward. Continue?",
                "Change Data Location", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                ChangeDataLocationButton.IsEnabled = false;
                DataLocationStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGray);
                DataLocationStatusTextBlock.Text = "Moving data...";

                AppConfig.RelocateDataDirectory(newDataDir);

                DataLocationStatusTextBlock.Foreground = System.Windows.Media.Brushes.LightGreen;
                DataLocationStatusTextBlock.Text = "Data moved. Restarting...";

                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                if (!string.IsNullOrEmpty(exePath))
                {
                    Process.Start(exePath);
                }
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                DataLocationStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                DataLocationStatusTextBlock.Text = "Failed to move data: " + ex.Message;
                ChangeDataLocationButton.IsEnabled = true;
            }
        }

        private void ClearAllNotesButton_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "This will permanently delete ALL notes, images, and attachments. This cannot be undone.\n\nAre you sure you want to continue?",
                "Clear All Notes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;

            var finalConfirm = MessageBox.Show(
                "Last chance -- this really cannot be undone. Delete everything?",
                "Clear All Notes", MessageBoxButton.YesNo, MessageBoxImage.Stop);
            if (finalConfirm != MessageBoxResult.Yes) return;

            try
            {
                DatabaseHelper.ClearAllNotes();

                if (Owner is MainWindow mainWin)
                {
                    mainWin.RefreshNotesList();
                    mainWin.RefreshTagsFilter();
                }
                else if (Application.Current.MainWindow is MainWindow main)
                {
                    main.RefreshNotesList();
                    main.RefreshTagsFilter();
                }

                MessageBox.Show("All notes have been cleared.", "Clear All Notes", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Failed to clear notes: " + ex.Message, "Clear All Notes", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Restore original opacity on cancel
            if (Owner is MainWindow main)
                main.ApplySidebarOpacity(_originalOpacity);

            this.DialogResult = false;
            this.Close();
        }

        private async void DockerSetupBtn_Click(object sender, RoutedEventArgs e)
        {
            DockerSetupBtn.IsEnabled = false;
            SetupStatusTextBlock.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LightGray);
            SetupStatusTextBlock.Text = "Checking if Docker is running...";

            try
            {
                // 1. Verify Docker is running
                int dockerCheck = await RunShellCommandAsync("docker", "info");
                if (dockerCheck != 0)
                {
                    SetupStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    SetupStatusTextBlock.Text = "Error: Docker Desktop is not running. Please start Docker and try again.";
                    DockerSetupBtn.IsEnabled = true;
                    return;
                }

                // 2. Check if Ollama container exists
                SetupStatusTextBlock.Text = "Starting Ollama container...";
                int startResult = await RunShellCommandAsync("docker", "start ollama");
                if (startResult != 0)
                {
                    // Container doesn't exist, create it
                    SetupStatusTextBlock.Text = "Creating and downloading Ollama container...";
                    int runResult = await RunShellCommandAsync("docker", "run -d -v ollama:/root/.ollama -p 11434:11434 --name ollama ollama/ollama");
                    if (runResult != 0)
                    {
                        SetupStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                        SetupStatusTextBlock.Text = "Error: Failed to run docker container. Check if port 11434 is already in use.";
                        DockerSetupBtn.IsEnabled = true;
                        return;
                    }
                }

                // 3. Wait for Ollama service to respond
                SetupStatusTextBlock.Text = "Waiting for Ollama service to start up...";
                bool isRunning = false;
                using (var client = new HttpClient())
                {
                    for (int i = 0; i < 15; i++)
                    {
                        try
                        {
                            var response = await client.GetAsync($"{OllamaUrlTextBox.Text.Trim()}/api/tags");
                            if (response.IsSuccessStatusCode)
                            {
                                isRunning = true;
                                break;
                            }
                        }
                        catch {}
                        await Task.Delay(1000);
                    }
                }

                if (!isRunning)
                {
                    SetupStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    SetupStatusTextBlock.Text = "Error: Ollama service did not respond. Check URL configs.";
                    DockerSetupBtn.IsEnabled = true;
                    return;
                }

                // 4. Pull Llama 3.2:1b via Ollama API
                SetupStatusTextBlock.Text = "Downloading Llama 3.2:1b model. Please wait, this takes some time...";

                string targetModel = "llama3.2:1b";
                bool pullSuccess = false;
                using (var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    var payload = new { name = targetModel, stream = false };
                    string json = JsonSerializer.Serialize(payload);
                    var payloadContent = new StringContent(json, Encoding.UTF8, "application/json");

                    var response = await client.PostAsync($"{OllamaUrlTextBox.Text.Trim()}/api/pull", payloadContent);
                    if (response.IsSuccessStatusCode)
                    {
                        pullSuccess = true;
                    }
                }

                if (pullSuccess)
                {
                    SetupStatusTextBlock.Foreground = System.Windows.Media.Brushes.LightGreen;
                    SetupStatusTextBlock.Text = $"AI setup complete! Model {targetModel} downloaded successfully.";
                    OllamaModelTextBox.Text = targetModel;
                }
                else
                {
                    SetupStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    SetupStatusTextBlock.Text = "Error: Ollama pulled request failed. Please check internet connection.";
                }
            }
            catch (Exception ex)
            {
                SetupStatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                SetupStatusTextBlock.Text = "Setup Exception: " + ex.Message;
            }
            finally
            {
                DockerSetupBtn.IsEnabled = true;
            }
        }

        private static Task<int> RunShellCommandAsync(string command, string arguments)
        {
            var tcs = new TaskCompletionSource<int>();
            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = command,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                },
                EnableRaisingEvents = true
            };

            process.Exited += (sender, args) =>
            {
                tcs.SetResult(process.ExitCode);
                process.Dispose();
            };

            try
            {
                process.Start();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }

            return tcs.Task;
        }
    }
}
