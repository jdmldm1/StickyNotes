using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace StickyNotes__
{
    public class TeamsTranscriptEntry
    {
        public string Timestamp { get; set; } = "";
        public string Speaker { get; set; } = "";
        public string Text { get; set; } = "";
    }

    public class TeamsMeetingData
    {
        public string Title { get; set; } = "Teams Meeting";
        public DateTime StartTime { get; set; } = DateTime.Now;
        public DateTime EndTime { get; set; } = DateTime.Now.AddMinutes(30);
        public string Organizer { get; set; } = "";
        public List<string> Attendees { get; set; } = new();
        public string JoinUrl { get; set; } = "";
        public string MeetingId { get; set; } = "";
        public string Passcode { get; set; } = "";
        public bool IsDoD { get; set; }
        public List<string> ActionItems { get; set; } = new();
        public List<TeamsTranscriptEntry> Transcript { get; set; } = new();
        public string NotesSummary { get; set; } = "";
    }

    public enum MicrosoftCloudEnvironment
    {
        Commercial,
        USGovDoD,
        USGovGCCHigh
    }

    public static class TeamsIntegrationHelper
    {
        private static readonly HttpClient _httpClient = CreateConfiguredHttpClient();

        private static HttpClient CreateConfiguredHttpClient()
        {
            var handler = new SocketsHttpHandler
            {
                UseProxy = true,
                Proxy = HttpClient.DefaultProxy,

                SslOptions = new SslClientAuthenticationOptions
                {
                    EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,

                    LocalCertificateSelectionCallback = (sender, targetHost, localCertificates, remoteCertificate, acceptableIssuers) =>
                    {
                        try
                        {
                            using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                            store.Open(OpenFlags.ReadOnly);
                            var certs = store.Certificates.Find(X509FindType.FindByKeyUsage, X509KeyUsageFlags.DigitalSignature, true);

                            if (acceptableIssuers != null && acceptableIssuers.Length > 0)
                            {
                                foreach (var cert in certs)
                                {
                                    if (acceptableIssuers.Any(issuer => string.Equals(cert.Issuer, issuer, StringComparison.OrdinalIgnoreCase)))
                                    {
                                        return cert;
                                    }
                                }
                            }

                            foreach (var cert in certs)
                            {
                                if (cert.Issuer.Contains("DoD", StringComparison.OrdinalIgnoreCase) ||
                                    cert.Subject.Contains("DoD", StringComparison.OrdinalIgnoreCase))
                                {
                                    return cert;
                                }
                            }
                        }
                        catch { }

                        return null;
                    }
                }
            };

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(45)
            };
        }

        public static (bool hasCerts, string description) CheckSmartcardPkiStatus()
        {
            try
            {
                using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
                store.Open(OpenFlags.ReadOnly);
                var dodCerts = new List<string>();

                foreach (var cert in store.Certificates)
                {
                    if (cert.Issuer.Contains("DoD", StringComparison.OrdinalIgnoreCase) ||
                        cert.Subject.Contains("DoD", StringComparison.OrdinalIgnoreCase) ||
                        cert.Issuer.Contains("DISA", StringComparison.OrdinalIgnoreCase))
                    {
                        string name = cert.GetNameInfo(X509NameType.SimpleName, false);
                        if (!string.IsNullOrEmpty(name) && !dodCerts.Contains(name))
                        {
                            dodCerts.Add(name);
                        }
                    }
                }

                if (dodCerts.Count > 0)
                {
                    return (true, $"✓ {dodCerts.Count} DoD Smartcard (CAC) cert(s) ready in Windows Keystore ({string.Join(", ", dodCerts.Take(2))})");
                }

                return (false, "No DoD certificates detected in CurrentUser\\My (insert CAC card or verify ActivClient/minidriver)");
            }
            catch (Exception ex)
            {
                return (false, "Certificate check notice: " + ex.Message);
            }
        }

        public static (string loginBase, string graphBase) GetCloudEndpoints(MicrosoftCloudEnvironment env)
        {
            return env switch
            {
                MicrosoftCloudEnvironment.USGovDoD or MicrosoftCloudEnvironment.USGovGCCHigh =>
                    ("https://login.microsoftonline.us", "https://graph.microsoft.us"),
                _ =>
                    ("https://login.microsoftonline.com", "https://graph.microsoft.com")
            };
        }

        public static TeamsMeetingData ParseVttTranscript(string vttContent, string? fallbackTitle = null)
        {
            var data = new TeamsMeetingData
            {
                Title = string.IsNullOrWhiteSpace(fallbackTitle) ? "Teams Meeting Notes" : fallbackTitle
            };

            if (vttContent.Contains("dod.teams.microsoft.us", StringComparison.OrdinalIgnoreCase) ||
                vttContent.Contains(".mil", StringComparison.OrdinalIgnoreCase))
            {
                data.IsDoD = true;
            }

            var speakersSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var actionItems = new List<string>();
            var entries = new List<TeamsTranscriptEntry>();

            string[] lines = vttContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            string currentTimestamp = "";
            string currentSpeaker = "";
            var currentText = new StringBuilder();

            var timeRegex = new Regex(@"(\d{2}:\d{2}(?::\d{2})?(?:\.\d{3})?)\s*-->\s*(\d{2}:\d{2}(?::\d{2})?(?:\.\d{3})?)", RegexOptions.Compiled);

            var voiceTagRegex = new Regex(@"<v\s+([^>]+)>(.*)", RegexOptions.Compiled);

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("WEBVTT", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(currentTimestamp) && currentText.Length > 0)
                    {
                        string text = currentText.ToString().Trim();
                        entries.Add(new TeamsTranscriptEntry
                        {
                            Timestamp = currentTimestamp,
                            Speaker = currentSpeaker,
                            Text = text
                        });

                        CheckForActionItem(currentSpeaker, text, actionItems);
                        currentText.Clear();
                    }
                    continue;
                }

                var timeMatch = timeRegex.Match(line);
                if (timeMatch.Success)
                {
                    if (!string.IsNullOrEmpty(currentTimestamp) && currentText.Length > 0)
                    {
                        string text = currentText.ToString().Trim();
                        entries.Add(new TeamsTranscriptEntry
                        {
                            Timestamp = currentTimestamp,
                            Speaker = currentSpeaker,
                            Text = text
                        });

                        CheckForActionItem(currentSpeaker, text, actionItems);
                        currentText.Clear();
                    }

                    currentTimestamp = timeMatch.Groups[1].Value;
                    continue;
                }

                var voiceMatch = voiceTagRegex.Match(line);
                if (voiceMatch.Success)
                {
                    currentSpeaker = voiceMatch.Groups[1].Value.Trim();
                    speakersSet.Add(currentSpeaker);
                    string textPart = voiceMatch.Groups[2].Value.Replace("</v>", "").Trim();
                    if (!string.IsNullOrEmpty(textPart))
                    {
                        if (currentText.Length > 0) currentText.Append(' ');
                        currentText.Append(textPart);
                    }
                }
                else
                {
                    string clean = Regex.Replace(line, "<[^>]+>", "").Trim();
                    if (!string.IsNullOrEmpty(clean))
                    {
                        if (currentText.Length > 0) currentText.Append(' ');
                        currentText.Append(clean);
                    }
                }
            }

            if (!string.IsNullOrEmpty(currentTimestamp) && currentText.Length > 0)
            {
                string text = currentText.ToString().Trim();
                entries.Add(new TeamsTranscriptEntry
                {
                    Timestamp = currentTimestamp,
                    Speaker = currentSpeaker,
                    Text = text
                });
                CheckForActionItem(currentSpeaker, text, actionItems);
            }

            data.Attendees = speakersSet.OrderBy(s => s).ToList();
            data.Transcript = entries;
            data.ActionItems = actionItems;

            return data;
        }

        public static TeamsMeetingData ParsePastedRecap(string rawText, string? fallbackTitle = null)
        {
            var data = new TeamsMeetingData
            {
                Title = string.IsNullOrWhiteSpace(fallbackTitle) ? "Teams Meeting" : fallbackTitle
            };

            if (rawText.Contains("dod.teams.microsoft.us", StringComparison.OrdinalIgnoreCase) ||
                rawText.Contains(".mil", StringComparison.OrdinalIgnoreCase))
            {
                data.IsDoD = true;
            }

            var joinMatch = Regex.Match(rawText, @"https?://(?:[a-zA-Z0-9.-]+\.)?teams\.microsoft\.(?:us|com)/[^\s\)\>]+|https?://teams\.apps\.mil/[^\s\)\>]+");
            if (joinMatch.Success)
            {
                data.JoinUrl = joinMatch.Value;
                if (data.JoinUrl.Contains("dod.teams.microsoft.us", StringComparison.OrdinalIgnoreCase) || data.JoinUrl.Contains(".mil", StringComparison.OrdinalIgnoreCase))
                {
                    data.IsDoD = true;
                }
            }

            var idMatch = Regex.Match(rawText, @"(?:Meeting\s+ID|Meeting-ID):\s*([0-9\s]+)", RegexOptions.IgnoreCase);
            if (idMatch.Success)
            {
                data.MeetingId = idMatch.Groups[1].Value.Trim();
            }
            var passMatch = Regex.Match(rawText, @"(?:Passcode|Pass\s+code):\s*([A-Za-z0-9]+)", RegexOptions.IgnoreCase);
            if (passMatch.Success)
            {
                data.Passcode = passMatch.Groups[1].Value.Trim();
            }

            var speakersSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var actionItems = new List<string>();
            var entries = new List<TeamsTranscriptEntry>();

            string[] lines = rawText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

            var speakerHeaderRegex = new Regex(@"^(?:\[?(\d{1,2}:\d{2}(?::\d{2})?)\]?\s+)?([A-Za-z0-9\s.',/()_-]+?)(?:\s+\[?(\d{1,2}:\d{2}(?::\d{2})?)\]?)?:?\s*$", RegexOptions.Compiled);
            var inlineSpeakerRegex = new Regex(@"^(?:\[?(\d{1,2}:\d{2}(?::\d{2})?)\]?\s+)?([A-Za-z0-9\s.',/()_-]+?):\s+(.+)$", RegexOptions.Compiled);

            string currentSpeaker = "Participant";
            string currentTimestamp = "";

            foreach (var rawLine in lines)
            {
                string line = rawLine.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                var inlineMatch = inlineSpeakerRegex.Match(line);
                if (inlineMatch.Success)
                {
                    string time = inlineMatch.Groups[1].Value;
                    string spk = inlineMatch.Groups[2].Value.Trim();
                    string text = inlineMatch.Groups[3].Value.Trim();

                    speakersSet.Add(spk);
                    entries.Add(new TeamsTranscriptEntry { Timestamp = time, Speaker = spk, Text = text });
                    CheckForActionItem(spk, text, actionItems);
                    continue;
                }

                var headerMatch = speakerHeaderRegex.Match(line);
                if (headerMatch.Success && line.Length < 60)
                {
                    currentTimestamp = !string.IsNullOrEmpty(headerMatch.Groups[1].Value)
                        ? headerMatch.Groups[1].Value
                        : headerMatch.Groups[3].Value;
                    currentSpeaker = headerMatch.Groups[2].Value.Trim();
                    speakersSet.Add(currentSpeaker);
                    continue;
                }

                entries.Add(new TeamsTranscriptEntry
                {
                    Timestamp = currentTimestamp,
                    Speaker = currentSpeaker,
                    Text = line
                });
                CheckForActionItem(currentSpeaker, line, actionItems);
            }

            data.Attendees = speakersSet.OrderBy(s => s).ToList();
            data.Transcript = entries;
            data.ActionItems = actionItems;

            return data;
        }

        private static void CheckForActionItem(string speaker, string text, List<string> actionItems)
        {
            if (string.IsNullOrWhiteSpace(text)) return;

            var actionTriggers = new[]
            {
                "action item", "todo", "i will", "i'll", "will follow up", "can you",
                "assigned to", "take a look", "need to", "please make sure", "by next", "deadline",
                "suspense", "tasker", "poc", "action officer", "due by", "will coordinate",
                "rfi", "deliverable", "milestone", "tracking", "coordination", "concur", "ai:", "ai -"
            };

            string lower = text.ToLowerInvariant();
            if (actionTriggers.Any(trigger => lower.Contains(trigger)))
            {
                string snippet = text.Length > 120 ? text.Substring(0, 117) + "..." : text;
                string item = string.IsNullOrEmpty(speaker) ? snippet : $"{speaker}: {snippet}";
                if (!actionItems.Contains(item))
                {
                    actionItems.Add(item);
                }
            }
        }

        public static string FormatAsStickyNote(TeamsMeetingData meeting)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"# 👥 {meeting.Title}");
            if (meeting.IsDoD)
            {
                sb.AppendLine("🛡️ **DoD Teams Meeting** (`dod.teams.microsoft.us`)");
            }
            sb.AppendLine($"📅 {meeting.StartTime:f}");
            if (!string.IsNullOrEmpty(meeting.Organizer))
            {
                sb.AppendLine($"👤 Organizer: {meeting.Organizer}");
            }
            if (meeting.Attendees.Count > 0)
            {
                sb.AppendLine($"👥 Attendees: {string.Join(", ", meeting.Attendees)}");
            }
            if (!string.IsNullOrEmpty(meeting.JoinUrl))
            {
                sb.AppendLine($"🔗 Teams Link: {meeting.JoinUrl}");
            }
            if (!string.IsNullOrEmpty(meeting.MeetingId))
            {
                string pc = !string.IsNullOrEmpty(meeting.Passcode) ? $" · Passcode: `{meeting.Passcode}`" : "";
                sb.AppendLine($"🆔 Meeting ID: `{meeting.MeetingId}`{pc}");
            }
            sb.AppendLine();

            sb.AppendLine("### 🎯 Action Items");
            if (meeting.ActionItems.Count > 0)
            {
                foreach (var item in meeting.ActionItems)
                {
                    sb.AppendLine($"- [ ] {item}");
                }
            }
            else
            {
                sb.AppendLine("- [ ] Follow up on meeting discussion");
            }
            sb.AppendLine();

            sb.AppendLine("### 📝 Key Discussion & Notes");
            if (meeting.Transcript.Count > 0)
            {
                string lastSpeaker = "";
                foreach (var entry in meeting.Transcript.Take(100))
                {
                    if (entry.Speaker != lastSpeaker)
                    {
                        sb.AppendLine();
                        string timeStr = string.IsNullOrEmpty(entry.Timestamp) ? "" : $" [{entry.Timestamp}]";
                        sb.AppendLine($"**{entry.Speaker}**{timeStr}:");
                        lastSpeaker = entry.Speaker;
                    }
                    sb.AppendLine($"> {entry.Text}");
                }

                if (meeting.Transcript.Count > 100)
                {
                    sb.AppendLine();
                    sb.AppendLine($"*(... {meeting.Transcript.Count - 100} additional transcript lines omitted for brevity)*");
                }
            }
            else if (!string.IsNullOrEmpty(meeting.NotesSummary))
            {
                sb.AppendLine(meeting.NotesSummary);
            }
            else
            {
                sb.AppendLine("- Summary of key talking points");
                sb.AppendLine("- Decisions reached");
            }

            return sb.ToString();
        }

        public static string GenerateBlufBrief(TeamsMeetingData meeting)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=======================================================================");
            sb.AppendLine(meeting.IsDoD ? "EXECUTIVE BRIEFING // BLUF // CUI" : "EXECUTIVE BRIEFING // BLUF");
            sb.AppendLine("=======================================================================");
            sb.AppendLine($"SUBJECT: {meeting.Title.ToUpperInvariant()}");
            sb.AppendLine($"DATE/TIME: {meeting.StartTime:dddd, dd MMM yyyy HH:mm} - {meeting.EndTime:HH:mm}");
            if (!string.IsNullOrEmpty(meeting.Organizer))
            {
                sb.AppendLine($"ORGANIZER: {meeting.Organizer}");
            }
            if (meeting.Attendees.Count > 0)
            {
                sb.AppendLine($"ATTENDEES: {string.Join(", ", meeting.Attendees)}");
            }
            if (!string.IsNullOrEmpty(meeting.JoinUrl))
            {
                sb.AppendLine($"TEAMS LINK: {meeting.JoinUrl}");
            }
            sb.AppendLine();

            sb.AppendLine("1. BLUF (BOTTOM LINE UP FRONT):");
            if (!string.IsNullOrEmpty(meeting.NotesSummary))
            {
                sb.AppendLine($"   {meeting.NotesSummary}");
            }
            else
            {
                sb.AppendLine("   Summary of primary objective, mission alignment, and leadership intent.");
            }
            sb.AppendLine();

            sb.AppendLine("2. KEY DECISIONS & DISCUSSION:");
            if (meeting.Transcript.Count > 0)
            {
                foreach (var item in meeting.Transcript.Take(6))
                {
                    sb.AppendLine($"   - {item.Speaker}: {item.Text}");
                }
            }
            else
            {
                sb.AppendLine("   - Alignment reached on primary schedule and operational deliverables.");
            }
            sb.AppendLine();

            sb.AppendLine("3. SUSPENSES & TASKERS (ACTION ITEMS):");
            if (meeting.ActionItems.Count > 0)
            {
                foreach (var ai in meeting.ActionItems)
                {
                    sb.AppendLine($"   - [ ] {ai}");
                }
            }
            else
            {
                sb.AppendLine("   - [ ] No immediate suspenses assigned.");
            }
            sb.AppendLine("=======================================================================");

            return sb.ToString();
        }

        #region Microsoft Graph API Integration

        public class DeviceCodeResponse
        {
            public string device_code { get; set; } = "";
            public string user_code { get; set; } = "";
            public string verification_uri { get; set; } = "";
            public int expires_in { get; set; } = 900;
            public int interval { get; set; } = 5;
            public string message { get; set; } = "";
        }

        public class TokenResponse
        {
            public string access_token { get; set; } = "";
            public string refresh_token { get; set; } = "";
            public string token_type { get; set; } = "";
            public int expires_in { get; set; }
            public string error { get; set; } = "";
        }

        public static async Task<DeviceCodeResponse?> StartDeviceCodeLoginAsync(
            string clientId,
            string tenantId = "common",
            MicrosoftCloudEnvironment env = MicrosoftCloudEnvironment.Commercial)
        {
            try
            {
                var (loginBase, graphBase) = GetCloudEndpoints(env);
                string endpoint = $"{loginBase}/{tenantId}/oauth2/v2.0/devicecode";
                var form = new Dictionary<string, string>
                {
                    { "client_id", clientId },
                    { "scope", $"{graphBase}/User.Read {graphBase}/Calendars.Read {graphBase}/OnlineMeetings.Read offline_access" }
                };

                using var response = await _httpClient.PostAsync(endpoint, new FormUrlEncodedContent(form));
                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<DeviceCodeResponse>(json);
            }
            catch
            {
                return null;
            }
        }

        public static async Task<string?> PollForTokenAsync(
            string clientId,
            string deviceCode,
            int intervalSeconds,
            CancellationToken cancellationToken,
            string tenantId = "common",
            MicrosoftCloudEnvironment env = MicrosoftCloudEnvironment.Commercial)
        {
            var (loginBase, _) = GetCloudEndpoints(env);
            string endpoint = $"{loginBase}/{tenantId}/oauth2/v2.0/token";
            DateTime deadline = DateTime.Now.AddMinutes(15);

            while (DateTime.Now < deadline && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(intervalSeconds * 1000, cancellationToken);
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    var form = new Dictionary<string, string>
                    {
                        { "grant_type", "urn:ietf:params:oauth:grant-type:device_code" },
                        { "client_id", clientId },
                        { "device_code", deviceCode }
                    };

                    using var response = await _httpClient.PostAsync(endpoint, new FormUrlEncodedContent(form), cancellationToken);
                    string json = await response.Content.ReadAsStringAsync(cancellationToken);
                    using var doc = JsonDocument.Parse(json);

                    if (doc.RootElement.TryGetProperty("access_token", out var tokenProp))
                    {
                        return tokenProp.GetString();
                    }

                    if (doc.RootElement.TryGetProperty("error", out var errorProp))
                    {
                        string err = errorProp.GetString() ?? "";
                        if (err == "authorization_pending")
                        {
                            continue;
                        }
                        if (err == "slow_down")
                        {
                            intervalSeconds += 5;
                            continue;
                        }

                        break;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        public static async Task<List<TeamsMeetingData>> FetchCalendarMeetingsAsync(
            string accessToken,
            MicrosoftCloudEnvironment env = MicrosoftCloudEnvironment.Commercial)
        {
            var meetings = new List<TeamsMeetingData>();

            try
            {
                var (_, graphBase) = GetCloudEndpoints(env);
                string url = $"{graphBase}/v1.0/me/calendar/events?$filter=isOnlineMeeting eq true&$select=id,subject,start,end,organizer,attendees,onlineMeeting,isOnlineMeeting&$orderby=start/dateTime desc&$top=15";
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                using var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return meetings;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("value", out var eventsArray))
                {
                    foreach (var ev in eventsArray.EnumerateArray())
                    {
                        var meeting = new TeamsMeetingData
                        {
                            Title = ev.TryGetProperty("subject", out var s) ? s.GetString() ?? "Teams Meeting" : "Teams Meeting"
                        };

                        if (ev.TryGetProperty("start", out var startProp) && startProp.TryGetProperty("dateTime", out var startDt))
                        {
                            if (DateTime.TryParse(startDt.GetString(), out var dt)) meeting.StartTime = dt;
                        }
                        if (ev.TryGetProperty("end", out var endProp) && endProp.TryGetProperty("dateTime", out var endDt))
                        {
                            if (DateTime.TryParse(endDt.GetString(), out var dt)) meeting.EndTime = dt;
                        }

                        if (ev.TryGetProperty("organizer", out var org) && org.TryGetProperty("emailAddress", out var orgEmail))
                        {
                            string name = orgEmail.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                            string address = orgEmail.TryGetProperty("address", out var a) ? a.GetString() ?? "" : "";
                            meeting.Organizer = string.IsNullOrEmpty(name) ? address : $"{name} ({address})";
                        }

                        if (ev.TryGetProperty("attendees", out var atts))
                        {
                            foreach (var att in atts.EnumerateArray())
                            {
                                if (att.TryGetProperty("emailAddress", out var ea))
                                {
                                    string name = ea.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                                    string address = ea.TryGetProperty("address", out var a) ? a.GetString() ?? "" : "";
                                    meeting.Attendees.Add(string.IsNullOrEmpty(name) ? address : name);
                                }
                            }
                        }

                        if (ev.TryGetProperty("onlineMeeting", out var om) && om.TryGetProperty("joinUrl", out var ju))
                        {
                            meeting.JoinUrl = ju.GetString() ?? "";
                        }

                        meetings.Add(meeting);
                    }
                }
            }
            catch { }

            return meetings;
        }

        #endregion
    }
}
