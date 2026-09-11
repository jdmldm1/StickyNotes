using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace StickyNotes__
{
    public partial class TeamsMeetingWindow : Window
    {
        private readonly MainWindow _mainWnd;
        private TeamsMeetingData _currentData = new();
        private CancellationTokenSource? _loginCts;
        private string? _deviceLoginUrl;

        private const string DefaultPublicClientId = "04b07795-8ddb-461a-bbee-02f9e1bf7b46";

        public TeamsMeetingWindow(MainWindow mainWnd)
        {
            InitializeComponent();
            _mainWnd = mainWnd;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(TranscriptInputTextBox.Text))
            {
                TranscriptInputTextBox.Text = @"WEBVTT

00:00:01.000 --> 00:00:05.000
<v Sarah Connor>Good morning team. Today we are aligning on the Q3 roadmap and delivery dates.</v>

00:00:06.000 --> 00:00:11.000
<v John Doe>I will complete the API integration and share the documentation by Friday.</v>

00:00:12.000 --> 00:00:16.500
<v Alex Rivera>Can you review the frontend design specs? I'll assign the Jira ticket to Sarah.</v>

00:00:17.000 --> 00:00:22.000
<v Sarah Connor>Sounds good. Let's make sure our deadline next Wednesday is met.</v>";
                AutoDetect();
            }

            RefreshSmartcardStatus();
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed) DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _loginCts?.Cancel();
            Close();
        }

        private void TabTranscript_Click(object sender, RoutedEventArgs e)
        {
            TranscriptImportPanel.Visibility = Visibility.Visible;
            CloudSyncPanel.Visibility = Visibility.Collapsed;
            TabTranscriptBtn.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30));
            TabTranscriptBtn.Foreground = Brushes.White;
            TabCloudBtn.Background = Brushes.Transparent;
            TabCloudBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB0));
        }

        private void TabCloud_Click(object sender, RoutedEventArgs e)
        {
            TranscriptImportPanel.Visibility = Visibility.Collapsed;
            CloudSyncPanel.Visibility = Visibility.Visible;
            TabCloudBtn.Background = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x30));
            TabCloudBtn.Foreground = Brushes.White;
            TabTranscriptBtn.Background = Brushes.Transparent;
            TabTranscriptBtn.Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xA0, 0xB0));
        }

        private void BrowseFile_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Teams Transcript File",
                Filter = "Teams Transcripts (*.vtt;*.txt)|*.vtt;*.txt|All Files (*.*)|*.*",
                DefaultExt = ".vtt"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(dlg.FileName);
                    TranscriptInputTextBox.Text = content;
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(dlg.FileName);
                    MeetingTitleTextBox.Text = nameWithoutExt.Replace('_', ' ').Replace('-', ' ');
                    AutoDetect();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Unable to read file: " + ex.Message, "File Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void TranscriptInput_TextChanged(object sender, TextChangedEventArgs e)
        {
            string text = TranscriptInputTextBox.Text;
            int lineCount = string.IsNullOrWhiteSpace(text) ? 0 : text.Split('\n').Length;
            TranscriptStatsText.Text = $"💬 {lineCount} lines";
        }

        private void AutoDetect_Click(object sender, RoutedEventArgs e)
        {
            AutoDetect();
        }

        private void AutoDetect()
        {
            string raw = TranscriptInputTextBox.Text;
            if (string.IsNullOrWhiteSpace(raw)) return;

            string title = MeetingTitleTextBox.Text.Trim();

            if (raw.Contains("WEBVTT", StringComparison.OrdinalIgnoreCase) || raw.Contains("-->"))
            {
                _currentData = TeamsIntegrationHelper.ParseVttTranscript(raw, title);
            }
            else
            {
                _currentData = TeamsIntegrationHelper.ParsePastedRecap(raw, title);
            }

            _currentData.Title = string.IsNullOrWhiteSpace(title) ? _currentData.Title : title;

            AttendeesWrapPanel.Children.Clear();
            if (_currentData.Attendees.Count == 0)
            {
                AttendeesWrapPanel.Children.Add(new TextBlock
                {
                    Text = "None detected (type speaker names with 'Name: ' in transcript)",
                    Foreground = Brushes.Gray,
                    FontSize = 10
                });
            }
            else
            {
                foreach (var att in _currentData.Attendees)
                {
                    var pill = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(40, 0, 132, 255)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(90, 0, 132, 255)),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(10),
                        Padding = new Thickness(6, 1, 6, 1),
                        Margin = new Thickness(0, 0, 4, 2)
                    };
                    pill.Child = new TextBlock
                    {
                        Text = "👤 " + att,
                        Foreground = new SolidColorBrush(Color.FromRgb(0x80, 0xD0, 0xFF)),
                        FontSize = 10
                    };
                    AttendeesWrapPanel.Children.Add(pill);
                }
            }

            ActionItemsBadgeText.Text = $"🎯 {_currentData.ActionItems.Count} action item{(_currentData.ActionItems.Count == 1 ? "" : "s")} detected";
            if (_currentData.IsDoD)
            {
                ImportStatusText.Text = $"🛡️ DoD Teams (dod.teams.microsoft.us): {_currentData.Attendees.Count} attendees, {_currentData.ActionItems.Count} action items.";
                if (SaveAsSecureCheck != null && SaveAsSecureCheck.IsChecked != true)
                {
                    SaveAsSecureCheck.IsChecked = true;
                }
            }
            else
            {
                ImportStatusText.Text = $"Detected {_currentData.Attendees.Count} attendees and {_currentData.ActionItems.Count} action items.";
            }
        }

        private void CreateStickyNote_Click(object sender, RoutedEventArgs e)
        {
            AutoDetect();

            string title = MeetingTitleTextBox.Text.Trim();
            if (string.IsNullOrEmpty(title)) title = "Teams Meeting Notes";
            _currentData.Title = title;

            string formattedContent = TeamsIntegrationHelper.FormatAsStickyNote(_currentData);

            bool makeSecure = SaveAsSecureCheck.IsChecked == true;
            string contentPackage;
            string noteColor = makeSecure ? "charcoal" : "blue";

            if (makeSecure)
            {
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
                    var unlockDialog = new PasswordDialog("Enter your vault password to encrypt this meeting note.", "Unlock Vault") { Owner = this };
                    if (unlockDialog.ShowDialog() != true) return;
                    if (!VaultService.TryUnlock(unlockDialog.Password))
                    {
                        MessageBox.Show("Incorrect password.", "Unlock Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }
                }

                string plainPackage = NoteContentHelper.BuildContentFromPlainText(formattedContent);
                contentPackage = VaultService.Encrypt(plainPackage);
            }
            else
            {
                contentPackage = NoteContentHelper.BuildContentFromPlainText(formattedContent);
            }

            int noteId = DatabaseHelper.CreateNote(
                title: title,
                content: contentPackage,
                imagePath: null,
                ocrText: null,
                color: noteColor,
                isSecure: makeSecure
            );

            var note = DatabaseHelper.GetNote(noteId);
            if (note != null)
            {
                note.Category = "Meeting";
                DatabaseHelper.UpdateNote(note);
            }

            try
            {
                DatabaseHelper.AddTagToNote(noteId, "meeting");
                DatabaseHelper.AddTagToNote(noteId, "teams");
                if (makeSecure)
                {
                    DatabaseHelper.AddTagToNote(noteId, "cui");
                    DatabaseHelper.AddTagToNote(noteId, "dod");
                }
            }
            catch { }

            _mainWnd.RefreshNotesList();
            _mainWnd.OpenNoteWindow(noteId);
            Close();
        }

        private void CopyBluf_Click(object sender, RoutedEventArgs e)
        {
            AutoDetect();
            try
            {
                string bluf = TeamsIntegrationHelper.GenerateBlufBrief(_currentData);
                Clipboard.SetText(bluf);
                if (sender is Button btn)
                {
                    string prev = btn.Content.ToString() ?? "📋 Copy BLUF";
                    btn.Content = "✓ Copied!";
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    t.Tick += (s, args) => { t.Stop(); btn.Content = prev; };
                    t.Start();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to copy BLUF: " + ex.Message, "Notice", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LaunchHud_Click(object sender, RoutedEventArgs e)
        {
            AutoDetect();
            string title = MeetingTitleTextBox.Text.Trim();
            if (string.IsNullOrEmpty(title)) title = "Active Meeting";
            _currentData.Title = title;

            string formattedContent = TeamsIntegrationHelper.FormatAsStickyNote(_currentData);
            string contentPackage = NoteContentHelper.BuildContentFromPlainText(formattedContent);
            int noteId = DatabaseHelper.CreateNote(
                title: title,
                content: contentPackage,
                imagePath: null,
                ocrText: null,
                color: "blue"
            );

            var hud = new MeetingHudWindow(_mainWnd, noteId, _currentData);
            hud.Show();
            Close();
        }

        #region Cloud Sync

        private MicrosoftCloudEnvironment GetSelectedCloudEnvironment()
        {
            if (CloudEnvironmentCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            {
                if (tag == "USGovDoD") return MicrosoftCloudEnvironment.USGovDoD;
                if (tag == "USGovGCCHigh") return MicrosoftCloudEnvironment.USGovGCCHigh;
            }
            return MicrosoftCloudEnvironment.Commercial;
        }

        private void CloudEnvironmentCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var env = GetSelectedCloudEnvironment();
            if (TenantLabelTextBlock == null || TenantIdTextBox == null || ClientIdTextBox == null || SignInBtn == null) return;

            switch (env)
            {
                case MicrosoftCloudEnvironment.USGovDoD:
                    TenantLabelTextBlock.Text = "DoD Tenant ID (GUID or 'organizations'):";
                    if (TenantIdTextBox.Text == "common") TenantIdTextBox.Text = "organizations";
                    SignInBtn.Content = "🔑 Sign in with DoD Account (dod.teams.microsoft.us)";
                    break;
                case MicrosoftCloudEnvironment.USGovGCCHigh:
                    TenantLabelTextBlock.Text = "GCC High Tenant ID (GUID or 'organizations'):";
                    if (TenantIdTextBox.Text == "common") TenantIdTextBox.Text = "organizations";
                    SignInBtn.Content = "🔑 Sign in with GCC High Account";
                    break;
                default:
                    TenantLabelTextBlock.Text = "Tenant ID (e.g. 'common' or corporate tenant GUID):";
                    if (TenantIdTextBox.Text == "organizations") TenantIdTextBox.Text = "common";
                    SignInBtn.Content = "🔑 Sign in with Microsoft 365";
                    break;
            }
        }

        private void ToggleAzureConfig_Click(object sender, RoutedEventArgs e)
        {
            bool opening = AzureConfigBorder.Visibility != Visibility.Visible;
            AzureConfigBorder.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
            if (opening) RefreshSmartcardStatus();
        }

        private void RefreshSmartcardStatus()
        {
            if (SmartcardStatusTextBlock == null) return;
            var (hasCerts, desc) = TeamsIntegrationHelper.CheckSmartcardPkiStatus();
            SmartcardStatusTextBlock.Text = desc;
            SmartcardStatusTextBlock.Foreground = hasCerts
                ? new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC))
                : new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
        }

        private async void SignInMicrosoft_Click(object sender, RoutedEventArgs e)
        {
            var env = GetSelectedCloudEnvironment();

            string clientId = string.IsNullOrWhiteSpace(ClientIdTextBox.Text)
                ? (env == MicrosoftCloudEnvironment.Commercial ? DefaultPublicClientId : "")
                : ClientIdTextBox.Text.Trim();

            if (string.IsNullOrEmpty(clientId) && env != MicrosoftCloudEnvironment.Commercial)
            {
                MessageBox.Show(
                    "Connecting to US Government / DoD sovereign clouds requires a registered Azure Government Client ID approved for your tenant.\n\nTip: You can use the 'Import Transcript' tab with any exported DoD Teams transcript (.vtt) without registering an Azure App or signing in!",
                    "Client ID Required for Sovereign Cloud",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string tenantId = string.IsNullOrWhiteSpace(TenantIdTextBox.Text)
                ? (env == MicrosoftCloudEnvironment.Commercial ? "common" : "organizations")
                : TenantIdTextBox.Text.Trim();

            SignInBtn.IsEnabled = false;
            SignInBtn.Content = "⏳ Requesting login code...";

            var resp = await TeamsIntegrationHelper.StartDeviceCodeLoginAsync(clientId, tenantId, env);
            if (resp == null)
            {
                SignInBtn.IsEnabled = true;
                SignInBtn.Content = env == MicrosoftCloudEnvironment.USGovDoD
                    ? "🔑 Sign in with US Gov DoD Account"
                    : "🔑 Sign in with Microsoft 365";
                MessageBox.Show(
                    "Unable to start Microsoft authentication.\n\nTip: You can paste your transcript or recap directly in the 'Import Transcript' tab without signing in!",
                    "Sign In Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            _deviceLoginUrl = resp.verification_uri;
            DeviceCodeTextBox.Text = resp.user_code;
            DeviceCodeInstructionText.Text = $"1. Open {resp.verification_uri}\n2. Enter code: {resp.user_code}\n3. Sign in to your Microsoft account.";
            DeviceCodeBanner.Visibility = Visibility.Visible;

            _loginCts = new CancellationTokenSource();
            SignInBtn.Content = "⏳ Waiting for browser sign-in...";

            string? token = await TeamsIntegrationHelper.PollForTokenAsync(clientId, resp.device_code, resp.interval, _loginCts.Token, tenantId, env);

            if (!string.IsNullOrEmpty(token))
            {
                DeviceCodeBanner.Visibility = Visibility.Collapsed;
                SignInBtn.Content = "✓ Connected to Microsoft 365";
                SignInBtn.Background = new SolidColorBrush(Color.FromRgb(0x16, 0x7a, 0x36));

                var meetings = await TeamsIntegrationHelper.FetchCalendarMeetingsAsync(token, env);
                RenderCloudMeetings(meetings);
            }
            else
            {
                SignInBtn.IsEnabled = true;
                SignInBtn.Content = env == MicrosoftCloudEnvironment.USGovDoD
                    ? "🔑 Sign in with US Gov DoD Account"
                    : "🔑 Sign in with Microsoft 365";
                DeviceCodeBanner.Visibility = Visibility.Collapsed;
            }
        }

        private void OpenDeviceLogin_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(DeviceCodeTextBox.Text);
                string url = string.IsNullOrEmpty(_deviceLoginUrl) ? "https://microsoft.com/devicelogin" : _deviceLoginUrl;
                if (SecurityHelper.IsSafeWebUri(url))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
            }
            catch { }
        }

        private void RenderCloudMeetings(List<TeamsMeetingData> meetings)
        {
            CloudMeetingsListPanel.Children.Clear();

            if (meetings.Count == 0)
            {
                CloudMeetingsListPanel.Children.Add(new TextBlock
                {
                    Text = "No online Teams meetings found on your calendar for the upcoming days.",
                    Foreground = Brushes.Gray,
                    FontSize = 11,
                    Margin = new Thickness(10)
                });
                return;
            }

            foreach (var m in meetings)
            {
                var card = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x1E, 0x26)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x3E)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 8, 10, 8),
                    Margin = new Thickness(0, 0, 0, 6)
                };

                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var infoStack = new StackPanel();
                Grid.SetColumn(infoStack, 0);

                infoStack.Children.Add(new TextBlock
                {
                    Text = m.Title,
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 12
                });

                string meta = $"📅 {m.StartTime:g} - {m.EndTime:t}";
                if (!string.IsNullOrEmpty(m.Organizer)) meta += $" · {m.Organizer}";
                if (m.Attendees.Count > 0) meta += $" · 👥 {m.Attendees.Count} attendees";

                infoStack.Children.Add(new TextBlock
                {
                    Text = meta,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9E, 0x9E, 0xAC)),
                    FontSize = 10,
                    Margin = new Thickness(0, 3, 0, 0)
                });

                grid.Children.Add(infoStack);

                var createBtn = new Button
                {
                    Content = "📝 Create Note",
                    Padding = new Thickness(10, 4, 10, 4),
                    Background = new SolidColorBrush(Color.FromRgb(0x00, 0x84, 0xFF)),
                    Foreground = Brushes.White,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand,
                    VerticalAlignment = VerticalAlignment.Center
                };
                createBtn.Resources.Add(typeof(Border), new Style(typeof(Border))
                {
                    Setters = { new Setter(Border.CornerRadiusProperty, new CornerRadius(4)) }
                });

                var captured = m;
                createBtn.Click += (s, args) =>
                {
                    string noteContent = TeamsIntegrationHelper.FormatAsStickyNote(captured);
                    string xamlPkg = NoteContentHelper.BuildContentFromPlainText(noteContent);
                    int newNoteId = DatabaseHelper.CreateNote(
                        title: captured.Title,
                        content: xamlPkg,
                        imagePath: null,
                        ocrText: null,
                        color: "blue"
                    );

                    var newNote = DatabaseHelper.GetNote(newNoteId);
                    if (newNote != null)
                    {
                        newNote.Category = "Meeting";
                        DatabaseHelper.UpdateNote(newNote);
                    }

                    try
                    {
                        DatabaseHelper.AddTagToNote(newNoteId, "meeting");
                        DatabaseHelper.AddTagToNote(newNoteId, "teams");
                    }
                    catch { }

                    _mainWnd.RefreshNotesList();
                    _mainWnd.OpenNoteWindow(newNoteId);
                    Close();
                };

                Grid.SetColumn(createBtn, 1);
                grid.Children.Add(createBtn);

                card.Child = grid;
                CloudMeetingsListPanel.Children.Add(card);
            }
        }

        #endregion
    }
}
