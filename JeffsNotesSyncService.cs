using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StickyNotes__
{
    public class JeffsNotesSyncResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public int Pulled { get; set; }
        public int Pushed { get; set; }
        public int DeletedLocally { get; set; }
        public int DeletedRemotely { get; set; }
        public int Conflicts { get; set; }
        public List<int> AffectedLocalNoteIds { get; } = new();
        public string? NewWatermark { get; set; }

        public string Summary()
        {
            if (!Success) return $"Sync failed: {Error}";
            if (Pulled == 0 && Pushed == 0 && DeletedLocally == 0 && DeletedRemotely == 0)
                return "Already up to date.";
            var parts = new List<string>();
            if (Pulled > 0) parts.Add($"{Pulled} pulled");
            if (Pushed > 0) parts.Add($"{Pushed} pushed");
            if (DeletedLocally > 0) parts.Add($"{DeletedLocally} removed locally");
            if (DeletedRemotely > 0) parts.Add($"{DeletedRemotely} removed remotely");
            if (Conflicts > 0) parts.Add($"{Conflicts} conflict(s) resolved in favor of JeffsNotes");
            return string.Join(", ", parts) + ".";
        }
    }

    public static class JeffsNotesSyncService
    {
        private class RemoteNoteDto
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("content")] public string Content { get; set; } = "";
            [JsonPropertyName("type")] public string? Type { get; set; }
            [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
            [JsonPropertyName("pinned")] public int Pinned { get; set; }
            [JsonPropertyName("is_template")] public int IsTemplate { get; set; }
            [JsonPropertyName("tags")] public string? Tags { get; set; }
            [JsonPropertyName("folder_id")] public string? FolderId { get; set; }
            [JsonPropertyName("deleted_at")] public string? DeletedAt { get; set; }
        }

        private class RemoteFolderDto
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
        }

        private class UploadResponse
        {
            [JsonPropertyName("url")] public string Url { get; set; } = "";
            [JsonPropertyName("name")] public string Name { get; set; } = "";
        }

        public static async Task<JeffsNotesSyncResult> SyncAsync(string baseUrl, string? lastSyncedAt)
        {
            var result = new JeffsNotesSyncResult();

            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                result.Error = "No JeffsNotes server URL configured.";
                return result;
            }

            baseUrl = baseUrl.TrimEnd('/');
            string watermark = string.IsNullOrEmpty(lastSyncedAt) ? "1970-01-01T00:00:00.000Z" : lastSyncedAt;

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

            List<RemoteFolderDto> remoteFolders;
            try
            {
                remoteFolders = await http.GetFromJsonAsync<List<RemoteFolderDto>>($"{baseUrl}/api/folders") ?? new();
            }
            catch (Exception ex)
            {
                result.Error = $"JeffsNotes server not reachable ({ex.Message}).";
                return result;
            }

            var folderIdToName = remoteFolders.ToDictionary(f => f.Id, f => f.Name);
            var folderNameToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in remoteFolders)
                if (!folderNameToId.ContainsKey(f.Name)) folderNameToId[f.Name] = f.Id;

            string newWatermark = watermark;
            void BumpWatermark(string? candidate)
            {
                if (!string.IsNullOrEmpty(candidate) && string.CompareOrdinal(candidate, newWatermark) > 0)
                    newWatermark = candidate;
            }

            try
            {
                var remoteChanged = await http.GetFromJsonAsync<List<RemoteNoteDto>>(
                    $"{baseUrl}/api/sync/notes?since={Uri.EscapeDataString(watermark)}") ?? new();

                var syncMapByRemote = DatabaseHelper.GetAllSyncMapByRemoteId();

                foreach (var rn in remoteChanged)
                {
                    try
                    {
                        BumpWatermark(rn.UpdatedAt);
                        BumpWatermark(rn.DeletedAt);
                        syncMapByRemote.TryGetValue(rn.Id, out var mapping);

                        if (rn.DeletedAt != null)
                        {
                            if (mapping != null)
                            {
                                DatabaseHelper.DeleteNote(mapping.LocalNoteId);
                                DatabaseHelper.DeleteSyncMapByLocalId(mapping.LocalNoteId);
                                result.DeletedLocally++;
                            }
                            continue;
                        }

                        string category = rn.FolderId != null && folderIdToName.TryGetValue(rn.FolderId, out var fname)
                            ? fname : "General";
                        var tagList = SplitTags(rn.Tags);

                        var localNote = mapping != null ? DatabaseHelper.GetNote(mapping.LocalNoteId) : null;

                        string? localImagePath = null;
                        string cleanedContent = rn.Content;
                        if (rn.Type == "image" || (!string.IsNullOrEmpty(rn.Content) && rn.Content.Contains("![Screenshot]")) || (!string.IsNullOrEmpty(rn.Content) && rn.Content.Contains("![")))
                        {
                            localImagePath = await DownloadRemoteImageAsync(http, baseUrl, rn.Content);
                            cleanedContent = RemoveMarkdownImage(rn.Content);
                        }

                        if (localNote == null)
                        {
                            if (mapping != null) DatabaseHelper.DeleteSyncMapByLocalId(mapping.LocalNoteId);

                            string content = NoteContentHelper.BuildContentFromPlainText(cleanedContent);
                            int localId = DatabaseHelper.CreateNote(rn.Title, content, localImagePath, null, "yellow");
                            var created = DatabaseHelper.GetNote(localId)!;
                            created.Category = category;
                            created.IsFavorite = rn.Pinned == 1;
                            created.IsTemplate = rn.IsTemplate == 1;
                            DatabaseHelper.UpdateNote(created);
                            foreach (var t in tagList) DatabaseHelper.AddTagToNote(localId, t);

                            DatabaseHelper.UpsertSyncMap(localId, rn.Id, BuildSignature(rn.Title, rn.Content, category, tagList, rn.Pinned == 1), rn.UpdatedAt);
                            result.Pulled++;
                            result.AffectedLocalNoteIds.Add(localId);
                            continue;
                        }

                        bool localDirty = BuildSignature(localNote.Title, NoteContentHelper.ExtractPlainText(localNote.Content), localNote.Category, DatabaseHelper.GetNoteTags(localNote.Id), localNote.IsFavorite)
                            != mapping!.LastSyncedSignature;

                        if (localDirty)
                        {
                            DatabaseHelper.AddNoteHistoryEntry(localNote.Id, localNote.Content);
                            result.Conflicts++;
                        }

                        localNote.Title = rn.Title;
                        localNote.Content = NoteContentHelper.BuildContentFromPlainText(cleanedContent);
                        localNote.ImagePath = localImagePath ?? localNote.ImagePath;
                        localNote.Category = category;
                        localNote.IsFavorite = rn.Pinned == 1;
                        localNote.IsTemplate = rn.IsTemplate == 1;
                        DatabaseHelper.UpdateNote(localNote);
                        DatabaseHelper.ClearNoteTags(localNote.Id);
                        foreach (var t in tagList) DatabaseHelper.AddTagToNote(localNote.Id, t);

                        DatabaseHelper.UpsertSyncMap(localNote.Id, rn.Id, BuildSignature(rn.Title, rn.Content, category, tagList, rn.Pinned == 1), rn.UpdatedAt);
                        result.Pulled++;
                        result.AffectedLocalNoteIds.Add(localNote.Id);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"JeffsNotes sync: failed to apply remote note {rn.Id}: {ex.Message}");
                    }
                }

                var justPulled = new HashSet<int>(result.AffectedLocalNoteIds);
                var syncMapByLocal = DatabaseHelper.GetAllSyncMapByLocalId();

                foreach (var note in DatabaseHelper.ListNotes())
                {
                    if (justPulled.Contains(note.Id)) continue;

                    try
                    {
                        syncMapByLocal.TryGetValue(note.Id, out var mapping);
                        var tags = DatabaseHelper.GetNoteTags(note.Id);
                        string plain = NoteContentHelper.ExtractPlainText(note.Content);

                        string contentToPush = plain;
                        string typeToPush = "text";

                        if (!string.IsNullOrEmpty(note.ImagePath) && File.Exists(note.ImagePath))
                        {
                            try
                            {
                                byte[] bytes = File.ReadAllBytes(note.ImagePath);
                                string base64 = Convert.ToBase64String(bytes);
                                string dataUri = $"data:image/png;base64,{base64}";
                                var uploadRes = await PostJsonAsync<UploadResponse>(http, $"{baseUrl}/api/upload", new 
                                { 
                                    image = dataUri, 
                                    name = Path.GetFileName(note.ImagePath) 
                                });
                                if (uploadRes != null && !string.IsNullOrEmpty(uploadRes.Url))
                                {
                                    contentToPush = $"![{Path.GetFileName(note.ImagePath)}]({uploadRes.Url})\n\n{plain}";
                                    typeToPush = "image";
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"JeffsNotes sync: failed to upload local screenshot: {ex.Message}");
                            }
                        }

                        string signature = BuildSignature(note.Title, contentToPush, note.Category, tags, note.IsFavorite);

                        if (mapping != null && signature == mapping.LastSyncedSignature) continue;

                        string? remoteFolderId = null;
                        if (!string.Equals(note.Category, "General", StringComparison.OrdinalIgnoreCase))
                        {
                            if (!folderNameToId.TryGetValue(note.Category, out remoteFolderId))
                            {
                                var createdFolder = await PostJsonAsync<RemoteFolderDto>(http, $"{baseUrl}/api/folders", new { name = note.Category });
                                if (createdFolder != null)
                                {
                                    remoteFolderId = createdFolder.Id;
                                    folderNameToId[note.Category] = createdFolder.Id;
                                    folderIdToName[createdFolder.Id] = note.Category;
                                }
                            }
                        }

                        RemoteNoteDto? pushed;
                        if (mapping == null)
                        {
                            string newRemoteId = Guid.NewGuid().ToString();
                            pushed = await PostJsonAsync<RemoteNoteDto>(http, $"{baseUrl}/api/notes", new
                            {
                                id = newRemoteId,
                                title = note.Title,
                                content = contentToPush,
                                type = typeToPush,
                                pinned = note.IsFavorite ? 1 : 0,
                                is_template = note.IsTemplate ? 1 : 0,
                                tags = string.Join(",", tags),
                                folder_id = remoteFolderId
                            });
                        }
                        else
                        {
                            pushed = await PatchJsonAsync<RemoteNoteDto>(http, $"{baseUrl}/api/notes/{mapping.RemoteId}", new
                            {
                                title = note.Title,
                                content = contentToPush,
                                type = typeToPush,
                                pinned = note.IsFavorite ? 1 : 0,
                                is_template = note.IsTemplate ? 1 : 0,
                                tags = string.Join(",", tags),
                                folder_id = remoteFolderId
                            });
                        }

                        if (pushed != null)
                        {
                            DatabaseHelper.UpsertSyncMap(note.Id, pushed.Id, signature, pushed.UpdatedAt);
                            BumpWatermark(pushed.UpdatedAt);
                            result.Pushed++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"JeffsNotes sync: failed to push local note {note.Id}: {ex.Message}");
                    }
                }

                foreach (var orphan in DatabaseHelper.GetOrphanedSyncMapEntries())
                {
                    try
                    {
                        var response = await http.DeleteAsync($"{baseUrl}/api/notes/{orphan.RemoteId}");
                        response.EnsureSuccessStatusCode();
                        result.DeletedRemotely++;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"JeffsNotes sync: failed to delete remote note {orphan.RemoteId}: {ex.Message}");
                    }
                    finally
                    {
                        DatabaseHelper.DeleteSyncMapByLocalId(orphan.LocalNoteId);
                    }
                }

                result.Success = true;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            result.NewWatermark = newWatermark;
            return result;
        }

        private static async Task<string?> DownloadRemoteImageAsync(HttpClient http, string baseUrl, string content)
        {
            try
            {
                var match = System.Text.RegularExpressions.Regex.Match(content, @"!\[.*?\]\((.*?)\)");
                if (match.Success)
                {
                    string imageUrl = match.Groups[1].Value;
                    if (imageUrl.StartsWith("/api/"))
                    {
                        string fullUrl = $"{baseUrl.TrimEnd('/')}{imageUrl}";
                        byte[] data = await http.GetByteArrayAsync(fullUrl);
                        string fileName = Path.GetFileName(imageUrl);
                        string localPath = Path.Combine(AppConfig.ImagesDir, fileName);
                        await File.WriteAllBytesAsync(localPath, data);
                        return localPath;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to download remote image: {ex.Message}");
            }
            return null;
        }

        private static string RemoveMarkdownImage(string content)
        {
            if (string.IsNullOrEmpty(content)) return "";
            return System.Text.RegularExpressions.Regex.Replace(content, @"!\[.*?\]\((.*?)\)\s*\r?\n?", "").Trim();
        }

        private static List<string> SplitTags(string? tags) =>
            (tags ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

        private static string BuildSignature(string title, string plainContent, string category, IEnumerable<string> tags, bool favorite)
        {
            const string Sep = "|";
            string sortedTags = string.Join(",", tags.Select(t => t.Trim().ToLowerInvariant()).OrderBy(t => t, StringComparer.Ordinal));
            return string.Join(Sep, title.Trim(), plainContent.Trim(), category.Trim(), sortedTags, favorite ? "1" : "0");
        }

        private static async Task<T?> PostJsonAsync<T>(HttpClient http, string url, object payload) where T : class
        {
            var response = await http.PostAsJsonAsync(url, payload);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>();
        }

        private static async Task<T?> PatchJsonAsync<T>(HttpClient http, string url, object payload) where T : class
        {
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url)
            {
                Content = JsonContent.Create(payload)
            };
            var response = await http.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<T>();
        }
    }
}
