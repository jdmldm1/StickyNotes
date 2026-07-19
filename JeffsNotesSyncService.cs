using System;
using System.Collections.Generic;
using System.Linq;
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

    // Two-way sync between StickyNotes++'s local SQLite store and a JeffsNotes web instance's
    // REST API. On conflict (a note changed on both sides since the last sync), JeffsNotes wins,
    // but the local edit is stashed into StickyNotes++'s own note history first so it isn't lost.
    //
    // Formatting note: JeffsNotes stores plain/markdown text; StickyNotes++ stores rich text
    // (XamlPackage). A note pulled from JeffsNotes becomes a plain-text note locally, and only
    // plain text is ever pushed back up -- rich formatting doesn't round-trip.
    public static class JeffsNotesSyncService
    {
        private class RemoteNoteDto
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
            [JsonPropertyName("title")] public string Title { get; set; } = "";
            [JsonPropertyName("content")] public string Content { get; set; } = "";
            [JsonPropertyName("updated_at")] public string? UpdatedAt { get; set; }
            [JsonPropertyName("pinned")] public int Pinned { get; set; }
            [JsonPropertyName("tags")] public string? Tags { get; set; }
            [JsonPropertyName("folder_id")] public string? FolderId { get; set; }
            [JsonPropertyName("deleted_at")] public string? DeletedAt { get; set; }
        }

        private class RemoteFolderDto
        {
            [JsonPropertyName("id")] public string Id { get; set; } = "";
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
                // Server unreachable (e.g. at work, off the home network) -- fail quietly, touch nothing.
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
                // --- Pull: remote notes changed (or soft-deleted) since the last sync ---
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

                        if (localNote == null)
                        {
                            // Either never seen before, or it was deleted locally but JeffsNotes still
                            // has a live (non-deleted) version -- JeffsNotes wins, so recreate it.
                            if (mapping != null) DatabaseHelper.DeleteSyncMapByLocalId(mapping.LocalNoteId);

                            string content = NoteContentHelper.BuildContentFromPlainText(rn.Content);
                            int localId = DatabaseHelper.CreateNote(rn.Title, content, null, null, "yellow");
                            var created = DatabaseHelper.GetNote(localId)!;
                            created.Category = category;
                            created.IsFavorite = rn.Pinned == 1;
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
                            // Real conflict: both sides changed. JeffsNotes wins, but stash the local
                            // edit into this note's own version history first so nothing is silently lost.
                            DatabaseHelper.AddNoteHistoryEntry(localNote.Id, localNote.Content);
                            result.Conflicts++;
                        }

                        localNote.Title = rn.Title;
                        localNote.Content = NoteContentHelper.BuildContentFromPlainText(rn.Content);
                        localNote.Category = category;
                        localNote.IsFavorite = rn.Pinned == 1;
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

                // --- Push: local notes that changed since they were last synced ---
                var justPulled = new HashSet<int>(result.AffectedLocalNoteIds);
                var syncMapByLocal = DatabaseHelper.GetAllSyncMapByLocalId();

                foreach (var note in DatabaseHelper.ListNotes())
                {
                    if (justPulled.Contains(note.Id)) continue; // already reconciled above this cycle

                    try
                    {
                        syncMapByLocal.TryGetValue(note.Id, out var mapping);
                        var tags = DatabaseHelper.GetNoteTags(note.Id);
                        string plain = NoteContentHelper.ExtractPlainText(note.Content);
                        string signature = BuildSignature(note.Title, plain, note.Category, tags, note.IsFavorite);

                        if (mapping != null && signature == mapping.LastSyncedSignature) continue; // no local change

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
                                content = plain,
                                pinned = note.IsFavorite ? 1 : 0,
                                tags = string.Join(",", tags),
                                folder_id = remoteFolderId
                            });
                        }
                        else
                        {
                            pushed = await PatchJsonAsync<RemoteNoteDto>(http, $"{baseUrl}/api/notes/{mapping.RemoteId}", new
                            {
                                title = note.Title,
                                content = plain,
                                pinned = note.IsFavorite ? 1 : 0,
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

                // --- Propagate local deletions: sync_map rows whose local note no longer exists ---
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
