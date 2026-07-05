using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using Microsoft.Data.Sqlite;

namespace StickyNotes__
{
    public static class AppConfig
    {
        public static readonly string AppName = "StickyNotesPlus";
        public static readonly string AppDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName);
        public static readonly string DbPath = Path.Combine(AppDir, "notes.db");
        public static readonly string ImagesDir = Path.Combine(AppDir, "images");
        public static readonly string AttachmentsDir = Path.Combine(AppDir, "attachments");
        public static readonly string SettingsPath = Path.Combine(AppDir, "settings.json");

        static AppConfig()
        {
            Directory.CreateDirectory(AppDir);
            Directory.CreateDirectory(ImagesDir);
            Directory.CreateDirectory(AttachmentsDir);
        }
    }

    public class Note
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Content { get; set; } = "";
        public string? ImagePath { get; set; }
        public string? OcrText { get; set; }
        public string Color { get; set; } = "yellow";
        public bool IsPinnedDesktop { get; set; }
        public bool IsPoppedOut { get; set; }
        public int? X { get; set; }
        public int? Y { get; set; }
        public int? W { get; set; }
        public int? H { get; set; }
        public double CanvasX { get; set; } = 50;
        public double CanvasY { get; set; } = 50;
        public string Category { get; set; } = "General";
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class NoteHistoryEntry
    {
        public int Id { get; set; }
        public int NoteId { get; set; }
        public string Content { get; set; } = "";
        public DateTime VersionedAt { get; set; }
    }

    public class NoteConnection
    {
        public int FromNoteId { get; set; }
        public int ToNoteId { get; set; }
    }

    public class NoteAttachment
    {
        public int Id { get; set; }
        public int NoteId { get; set; }
        public string FileName { get; set; } = "";
        public string FilePath { get; set; } = "";
        public DateTime AddedAt { get; set; }
    }

    public static class DatabaseHelper
    {
        private static string GetConnectionString() => $"Data Source={AppConfig.DbPath}";

        public static void InitDatabase()
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                
                // Create notes table
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS notes (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        title TEXT,
                        content TEXT,
                        image_path TEXT,
                        ocr_text TEXT,
                        color TEXT DEFAULT 'yellow',
                        is_pinned_desktop INTEGER DEFAULT 0,
                        is_popped_out INTEGER DEFAULT 0,
                        x INTEGER,
                        y INTEGER,
                        w INTEGER,
                        h INTEGER,
                        created_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        updated_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP
                    );
                ";
                cmd.ExecuteNonQuery();

                // Create tags table
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS tags (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        name TEXT UNIQUE NOT NULL
                    );
                ";
                cmd.ExecuteNonQuery();

                // Create note_tags association table
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS note_tags (
                        note_id INTEGER,
                        tag_id INTEGER,
                        PRIMARY KEY (note_id, tag_id),
                        FOREIGN KEY (note_id) REFERENCES notes(id) ON DELETE CASCADE,
                        FOREIGN KEY (tag_id) REFERENCES tags(id) ON DELETE CASCADE
                    );
                ";
                cmd.ExecuteNonQuery();

                // Create trigger to auto update updated_at timestamp
                cmd.CommandText = @"
                    CREATE TRIGGER IF NOT EXISTS update_note_timestamp 
                    AFTER UPDATE ON notes
                    BEGIN
                        UPDATE notes SET updated_at = CURRENT_TIMESTAMP WHERE id = new.id;
                    END;
                ";
                cmd.ExecuteNonQuery();

                // Create note_history table
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS note_history (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        note_id INTEGER,
                        content TEXT,
                        versioned_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (note_id) REFERENCES notes(id) ON DELETE CASCADE
                    );
                ";
                cmd.ExecuteNonQuery();

                // Create note_connections table
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS note_connections (
                        from_note_id INTEGER,
                        to_note_id INTEGER,
                        PRIMARY KEY (from_note_id, to_note_id),
                        FOREIGN KEY (from_note_id) REFERENCES notes(id) ON DELETE CASCADE,
                        FOREIGN KEY (to_note_id) REFERENCES notes(id) ON DELETE CASCADE
                    );
                ";
                cmd.ExecuteNonQuery();

                // Create note_attachments table (arbitrary files/documents dropped or attached to a note)
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS note_attachments (
                        id INTEGER PRIMARY KEY AUTOINCREMENT,
                        note_id INTEGER,
                        file_name TEXT,
                        file_path TEXT,
                        added_at TIMESTAMP DEFAULT CURRENT_TIMESTAMP,
                        FOREIGN KEY (note_id) REFERENCES notes(id) ON DELETE CASCADE
                    );
                ";
                cmd.ExecuteNonQuery();

                try
                {
                    cmd.CommandText = "ALTER TABLE notes ADD COLUMN canvas_x REAL DEFAULT 50;";
                    cmd.ExecuteNonQuery();
                }
                catch {}

                try
                {
                    cmd.CommandText = "ALTER TABLE notes ADD COLUMN canvas_y REAL DEFAULT 50;";
                    cmd.ExecuteNonQuery();
                }
                catch {}

                try
                {
                    cmd.CommandText = "ALTER TABLE notes ADD COLUMN category TEXT DEFAULT 'General';";
                    cmd.ExecuteNonQuery();
                }
                catch {}

                try
                {
                    // Searchable plain-text mirror of `content`. Content is stored as Base64-encoded
                    // XamlPackage (needed so embedded formatting round-trips correctly), which makes
                    // a plain SQL LIKE against it useless for finding words in a note's body -- this
                    // column is what search actually matches against, kept in sync by
                    // CreateNote/UpdateNote.
                    cmd.CommandText = "ALTER TABLE notes ADD COLUMN plain_text TEXT;";
                    cmd.ExecuteNonQuery();
                }
                catch {}

                // One-time cleanup: scrub any \id=... metadata tags from previously imported notes
                CleanupStickyNotesMetadataInDb(conn);

                // Backfill plain_text for notes saved before that column existed.
                BackfillPlainText(conn);
            }
        }

        private static void BackfillPlainText(SqliteConnection conn)
        {
            try
            {
                var toBackfill = new List<(int id, string content)>();
                var selectCmd = conn.CreateCommand();
                selectCmd.CommandText = "SELECT id, content FROM notes WHERE plain_text IS NULL;";
                using (var reader = selectCmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        toBackfill.Add((reader.GetInt32(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
                    }
                }

                foreach (var (id, content) in toBackfill)
                {
                    string plainText = NoteContentHelper.ExtractPlainText(content);
                    var updateCmd = conn.CreateCommand();
                    updateCmd.CommandText = "UPDATE notes SET plain_text = $plain_text WHERE id = $id;";
                    updateCmd.Parameters.AddWithValue("$plain_text", plainText);
                    updateCmd.Parameters.AddWithValue("$id", id);
                    updateCmd.ExecuteNonQuery();
                }
            }
            catch { /* Non-critical -- search just falls back to title/OCR matches for these rows */ }
        }

        private static void CleanupStickyNotesMetadataInDb(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            try
            {
                // Fetch notes whose title or content contains a \id= marker
                var checkCmd = conn.CreateCommand();
                checkCmd.CommandText = "SELECT id, title, content FROM notes WHERE title LIKE '%\\id=%' OR content LIKE '%\\id=%' OR title LIKE '%\\np=%' OR title LIKE '%\\li=%';";
                var toClean = new System.Collections.Generic.List<(int id, string title, string content)>();
                using (var r = checkCmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        int nid = r.GetInt32(0);
                        string t = r.IsDBNull(1) ? "" : r.GetString(1);
                        string c = r.IsDBNull(2) ? "" : r.GetString(2);
                        toClean.Add((nid, t, c));
                    }
                }

                var metaRegex = new System.Text.RegularExpressions.Regex(
                    @"\\(?:id|np|li|wi|ts|bidi|lnspc)=[^\s\\]*\s?",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                foreach (var (nid, title, content) in toClean)
                {
                    string cleanTitle   = metaRegex.Replace(title,   "").Trim();
                    string cleanContent = metaRegex.Replace(content, "").Trim();
                    if (cleanTitle == title && cleanContent == content) continue;

                    var upd = conn.CreateCommand();
                    upd.CommandText = "UPDATE notes SET title = $t, content = $c WHERE id = $id;";
                    upd.Parameters.AddWithValue("$t",   cleanTitle);
                    upd.Parameters.AddWithValue("$c",   cleanContent);
                    upd.Parameters.AddWithValue("$id",  nid);
                    upd.ExecuteNonQuery();
                }
            }
            catch { /* Non-critical — don't crash startup */ }
        }

        public static int CreateNote(string title = "", string content = "", string? imagePath = null, string? ocrText = null, string color = "yellow")
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO notes (title, content, plain_text, image_path, ocr_text, color)
                    VALUES ($title, $content, $plain_text, $image_path, $ocr_text, $color);
                    SELECT last_insert_rowid();
                ";
                cmd.Parameters.AddWithValue("$title", title);
                cmd.Parameters.AddWithValue("$content", content);
                cmd.Parameters.AddWithValue("$plain_text", NoteContentHelper.ExtractPlainText(content));
                cmd.Parameters.AddWithValue("$image_path", (object?)imagePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ocr_text", (object?)ocrText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$color", color);

                return Convert.ToInt32(cmd.ExecuteScalar());
            }
        }

        public static void UpdateNote(Note note)
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    UPDATE notes SET
                        title = $title,
                        content = $content,
                        plain_text = $plain_text,
                        image_path = $image_path,
                        ocr_text = $ocr_text,
                        color = $color,
                        is_pinned_desktop = $is_pinned,
                        is_popped_out = $is_popped,
                        x = $x,
                        y = $y,
                        w = $w,
                        h = $h,
                        canvas_x = $canvas_x,
                        canvas_y = $canvas_y,
                        category = $category
                    WHERE id = $id;
                ";
                cmd.Parameters.AddWithValue("$title", note.Title);
                cmd.Parameters.AddWithValue("$content", note.Content);
                cmd.Parameters.AddWithValue("$plain_text", NoteContentHelper.ExtractPlainText(note.Content));
                cmd.Parameters.AddWithValue("$image_path", (object?)note.ImagePath ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$ocr_text", (object?)note.OcrText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$color", note.Color);
                cmd.Parameters.AddWithValue("$is_pinned", note.IsPinnedDesktop ? 1 : 0);
                cmd.Parameters.AddWithValue("$is_popped", note.IsPoppedOut ? 1 : 0);
                cmd.Parameters.AddWithValue("$x", (object?)note.X ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$y", (object?)note.Y ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$w", (object?)note.W ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$h", (object?)note.H ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$canvas_x", note.CanvasX);
                cmd.Parameters.AddWithValue("$canvas_y", note.CanvasY);
                cmd.Parameters.AddWithValue("$category", note.Category);
                cmd.Parameters.AddWithValue("$id", note.Id);

                cmd.ExecuteNonQuery();
            }
        }

        public static void DeleteNote(int id)
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM notes WHERE id = $id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();

                // Clean up any tags that were only used by the note we just deleted.
                var cleanupCmd = conn.CreateCommand();
                cleanupCmd.CommandText = "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM note_tags);";
                cleanupCmd.ExecuteNonQuery();
            }
        }

        public static Note? GetNote(int id)
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT * FROM notes WHERE id = $id;";
                cmd.Parameters.AddWithValue("$id", id);

                using (var reader = cmd.ExecuteReader())
                {
                    if (reader.Read())
                    {
                        return ReadNote(reader);
                    }
                }
            }
            return null;
        }

        public static List<Note> ListNotes(string? searchQuery = null, string? tagFilter = null, string? categoryFilter = null, DateTime? updatedSince = null)
        {
            var notes = new List<Note>();
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();

                string query = "SELECT DISTINCT n.* FROM notes n";
                var conditions = new List<string>();

                if (!string.IsNullOrEmpty(tagFilter))
                {
                    query += " JOIN note_tags nt ON n.id = nt.note_id JOIN tags t ON nt.tag_id = t.id";
                    conditions.Add("t.name = $tag");
                    cmd.Parameters.AddWithValue("$tag", tagFilter.Trim().ToLower());
                }

                if (!string.IsNullOrEmpty(searchQuery))
                {
                    // plain_text is a searchable mirror of `content` (which is Base64-encoded
                    // XamlPackage and not itself searchable) kept in sync by CreateNote/UpdateNote.
                    conditions.Add("(n.title LIKE $search OR n.plain_text LIKE $search OR n.ocr_text LIKE $search)");
                    cmd.Parameters.AddWithValue("$search", $"%{searchQuery}%");
                }

                if (!string.IsNullOrEmpty(categoryFilter))
                {
                    conditions.Add("n.category = $category");
                    cmd.Parameters.AddWithValue("$category", categoryFilter);
                }

                if (updatedSince != null)
                {
                    conditions.Add("n.updated_at >= $updated_since");
                    cmd.Parameters.AddWithValue("$updated_since", updatedSince.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                }

                if (conditions.Count > 0)
                {
                    query += " WHERE " + string.Join(" AND ", conditions);
                }

                query += " ORDER BY n.is_pinned_desktop DESC, n.updated_at DESC";
                cmd.CommandText = query;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        notes.Add(ReadNote(reader));
                    }
                }
            }
            return notes;
        }

        public static void AddTagToNote(int noteId, string tagName)
        {
            tagName = tagName.Trim().ToLower();
            if (string.IsNullOrEmpty(tagName)) return;

            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var transaction = conn.BeginTransaction();
                try
                {
                    // Ensure tag exists
                    var cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "INSERT OR IGNORE INTO tags (name) VALUES ($name);";
                    cmd.Parameters.AddWithValue("$name", tagName);
                    cmd.ExecuteNonQuery();

                    // Get tag ID
                    cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "SELECT id FROM tags WHERE name = $name;";
                    cmd.Parameters.AddWithValue("$name", tagName);
                    int tagId = Convert.ToInt32(cmd.ExecuteScalar());

                    // Associate note and tag
                    cmd = conn.CreateCommand();
                    cmd.Transaction = transaction;
                    cmd.CommandText = "INSERT OR IGNORE INTO note_tags (note_id, tag_id) VALUES ($noteId, $tagId);";
                    cmd.Parameters.AddWithValue("$noteId", noteId);
                    cmd.Parameters.AddWithValue("$tagId", tagId);
                    cmd.ExecuteNonQuery();

                    transaction.Commit();
                }
                catch
                {
                    transaction.Rollback();
                    throw;
                }
            }
        }

        public static void RemoveTagFromNote(int noteId, string tagName)
        {
            tagName = tagName.Trim().ToLower();
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM note_tags 
                    WHERE note_id = $noteId AND tag_id = (SELECT id FROM tags WHERE name = $name);
                ";
                cmd.Parameters.AddWithValue("$noteId", noteId);
                cmd.Parameters.AddWithValue("$name", tagName);
                cmd.ExecuteNonQuery();

                // Clean up orphan tags
                cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM note_tags);";
                cmd.ExecuteNonQuery();
            }
        }

        public static List<string> GetNoteTags(int noteId)
        {
            var tags = new List<string>();
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT t.name FROM tags t
                    JOIN note_tags nt ON t.id = nt.tag_id
                    WHERE nt.note_id = $noteId
                    ORDER BY t.name ASC;
                ";
                cmd.Parameters.AddWithValue("$noteId", noteId);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tags.Add(reader.GetString(0));
                    }
                }
            }
            return tags;
        }

        public static List<string> ListAllTags()
        {
            var tags = new List<string>();
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT DISTINCT t.name FROM tags t
                    JOIN note_tags nt ON nt.tag_id = t.id
                    ORDER BY t.name ASC;
                ";

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        tags.Add(reader.GetString(0));
                    }
                }
            }
            return tags;
        }

        #region Note History & Connections Queries

        public static void AddNoteHistoryEntry(int noteId, string content)
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO note_history (note_id, content)
                    VALUES ($note_id, $content);
                ";
                cmd.Parameters.AddWithValue("$note_id", noteId);
                cmd.Parameters.AddWithValue("$content", content);
                cmd.ExecuteNonQuery();

                var cleanupCmd = conn.CreateCommand();
                cleanupCmd.CommandText = @"
                    DELETE FROM note_history 
                    WHERE note_id = $note_id AND id NOT IN (
                        SELECT id FROM note_history 
                        WHERE note_id = $note_id 
                        ORDER BY versioned_at DESC LIMIT 10
                    );
                ";
                cleanupCmd.Parameters.AddWithValue("$note_id", noteId);
                cleanupCmd.ExecuteNonQuery();
            }
        }

        public static List<NoteHistoryEntry> GetNoteHistory(int noteId)
        {
            var list = new List<NoteHistoryEntry>();
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, note_id, content, versioned_at 
                    FROM note_history 
                    WHERE note_id = $note_id 
                    ORDER BY versioned_at DESC;
                ";
                cmd.Parameters.AddWithValue("$note_id", noteId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new NoteHistoryEntry
                        {
                            Id = reader.GetInt32(0),
                            NoteId = reader.GetInt32(1),
                            Content = reader.GetString(2),
                            VersionedAt = DateTime.Parse(reader.GetString(3))
                        });
                    }
                }
            }
            return list;
        }

        public static void AddNoteConnection(int fromNoteId, int toNoteId)
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT OR IGNORE INTO note_connections (from_note_id, to_note_id)
                    VALUES ($from, $to);
                ";
                cmd.Parameters.AddWithValue("$from", fromNoteId);
                cmd.Parameters.AddWithValue("$to", toNoteId);
                cmd.ExecuteNonQuery();
            }
        }

        public static void RemoveNoteConnection(int fromNoteId, int toNoteId)
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    DELETE FROM note_connections 
                    WHERE (from_note_id = $from AND to_note_id = $to) 
                       OR (from_note_id = $to AND to_note_id = $from);
                ";
                cmd.Parameters.AddWithValue("$from", fromNoteId);
                cmd.Parameters.AddWithValue("$to", toNoteId);
                cmd.ExecuteNonQuery();
            }
        }

        public static List<NoteConnection> GetNoteConnections()
        {
            var list = new List<NoteConnection>();
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT from_note_id, to_note_id FROM note_connections;";
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new NoteConnection
                        {
                            FromNoteId = reader.GetInt32(0),
                            ToNoteId = reader.GetInt32(1)
                        });
                    }
                }
            }
            return list;
        }

        #endregion

        #region Note Attachments

        public static NoteAttachment AddAttachment(int noteId, string sourceFilePath)
        {
            string fileName = Path.GetFileName(sourceFilePath);
            string storedFileName = $"{noteId}_{Guid.NewGuid():N}_{fileName}";
            string storedPath = Path.Combine(AppConfig.AttachmentsDir, storedFileName);
            File.Copy(sourceFilePath, storedPath, overwrite: true);

            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO note_attachments (note_id, file_name, file_path)
                    VALUES ($note_id, $file_name, $file_path);
                    SELECT last_insert_rowid();
                ";
                cmd.Parameters.AddWithValue("$note_id", noteId);
                cmd.Parameters.AddWithValue("$file_name", fileName);
                cmd.Parameters.AddWithValue("$file_path", storedPath);
                int id = Convert.ToInt32(cmd.ExecuteScalar());

                return new NoteAttachment { Id = id, NoteId = noteId, FileName = fileName, FilePath = storedPath, AddedAt = DateTime.Now };
            }
        }

        public static List<NoteAttachment> GetNoteAttachments(int noteId)
        {
            var list = new List<NoteAttachment>();
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, note_id, file_name, file_path, added_at
                    FROM note_attachments
                    WHERE note_id = $note_id
                    ORDER BY added_at ASC;
                ";
                cmd.Parameters.AddWithValue("$note_id", noteId);
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        list.Add(new NoteAttachment
                        {
                            Id = reader.GetInt32(0),
                            NoteId = reader.GetInt32(1),
                            FileName = reader.GetString(2),
                            FilePath = reader.GetString(3),
                            AddedAt = DateTime.Parse(reader.GetString(4))
                        });
                    }
                }
            }
            return list;
        }

        public static void DeleteAttachment(int attachmentId)
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();

                var selectCmd = conn.CreateCommand();
                selectCmd.CommandText = "SELECT file_path FROM note_attachments WHERE id = $id;";
                selectCmd.Parameters.AddWithValue("$id", attachmentId);
                var filePath = selectCmd.ExecuteScalar() as string;

                var deleteCmd = conn.CreateCommand();
                deleteCmd.CommandText = "DELETE FROM note_attachments WHERE id = $id;";
                deleteCmd.Parameters.AddWithValue("$id", attachmentId);
                deleteCmd.ExecuteNonQuery();

                if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
                {
                    try { File.Delete(filePath); } catch { }
                }
            }
        }

        #endregion

        private static Note ReadNote(SqliteDataReader reader)
        {
            return new Note
            {
                Id = reader.GetInt32(reader.GetOrdinal("id")),
                Title = reader.IsDBNull(reader.GetOrdinal("title")) ? "" : reader.GetString(reader.GetOrdinal("title")),
                Content = reader.IsDBNull(reader.GetOrdinal("content")) ? "" : reader.GetString(reader.GetOrdinal("content")),
                ImagePath = reader.IsDBNull(reader.GetOrdinal("image_path")) ? null : reader.GetString(reader.GetOrdinal("image_path")),
                OcrText = reader.IsDBNull(reader.GetOrdinal("ocr_text")) ? null : reader.GetString(reader.GetOrdinal("ocr_text")),
                Color = reader.IsDBNull(reader.GetOrdinal("color")) ? "yellow" : reader.GetString(reader.GetOrdinal("color")),
                IsPinnedDesktop = reader.GetInt32(reader.GetOrdinal("is_pinned_desktop")) == 1,
                IsPoppedOut = reader.GetInt32(reader.GetOrdinal("is_popped_out")) == 1,
                X = reader.IsDBNull(reader.GetOrdinal("x")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("x")),
                Y = reader.IsDBNull(reader.GetOrdinal("y")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("y")),
                W = reader.IsDBNull(reader.GetOrdinal("w")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("w")),
                H = reader.IsDBNull(reader.GetOrdinal("h")) ? null : (int?)reader.GetInt32(reader.GetOrdinal("h")),
                CanvasX = GetDoubleSafe(reader, "canvas_x"),
                CanvasY = GetDoubleSafe(reader, "canvas_y"),
                Category = GetStringSafe(reader, "category"),
                CreatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("created_at"))),
                UpdatedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("updated_at")))
            };
        }

        private static string GetStringSafe(SqliteDataReader reader, string columnName, string defaultValue = "General")
        {
            try
            {
                int idx = reader.GetOrdinal(columnName);
                return reader.IsDBNull(idx) ? defaultValue : reader.GetString(idx);
            }
            catch
            {
                return defaultValue;
            }
        }

        private static double GetDoubleSafe(SqliteDataReader reader, string columnName, double defaultValue = 50)
        {
            try
            {
                int idx = reader.GetOrdinal(columnName);
                return reader.IsDBNull(idx) ? defaultValue : Convert.ToDouble(reader.GetValue(idx));
            }
            catch
            {
                return defaultValue;
            }
        }

        public static void ClearNoteTags(int noteId)
        {
            using (var conn = new SqliteConnection(GetConnectionString()))
            {
                conn.Open();
                var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM note_tags WHERE note_id = $noteId;";
                cmd.Parameters.AddWithValue("$noteId", noteId);
                cmd.ExecuteNonQuery();

                // Clean up orphan tags
                cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM tags WHERE id NOT IN (SELECT DISTINCT tag_id FROM note_tags);";
                cmd.ExecuteNonQuery();
            }
        }

        public static int ImportFromMicrosoftStickyNotes()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string plumPath = Path.Combine(localAppData, @"Packages\Microsoft.MicrosoftStickyNotes_8wekyb3d8bbwe\LocalState\plum.sqlite");

            if (!File.Exists(plumPath))
            {
                throw new FileNotFoundException("Microsoft Sticky Notes database plum.sqlite not found.", plumPath);
            }

            string tempDb = Path.Combine(Path.GetTempPath(), "plum_temp.sqlite");
            File.Copy(plumPath, tempDb, true);

            try
            {
                if (File.Exists(plumPath + "-wal"))
                    File.Copy(plumPath + "-wal", tempDb + "-wal", true);
                if (File.Exists(plumPath + "-shm"))
                    File.Copy(plumPath + "-shm", tempDb + "-shm", true);
            }
            catch {}

            int importCount = 0;

            using (var conn = new SqliteConnection($"Data Source={tempDb}"))
            {
                conn.Open();

                // Probe for which columns actually exist in the Note table
                // (Microsoft has changed the schema across app versions)
                var probeCmd = conn.CreateCommand();
                probeCmd.CommandText = "PRAGMA table_info(Note);";
                var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var probeReader = probeCmd.ExecuteReader())
                {
                    while (probeReader.Read())
                    {
                        // Column 1 in PRAGMA table_info is 'name'
                        string colName = probeReader.IsDBNull(1) ? "" : probeReader.GetString(1);
                        if (!string.IsNullOrEmpty(colName))
                            existingColumns.Add(colName);
                    }
                }

                if (existingColumns.Count == 0)
                {
                    // Table might not exist; nothing to import
                    return 0;
                }

                bool hasColor = existingColumns.Contains("Color");
                bool hasText  = existingColumns.Contains("Text");
                bool hasIsDeleted = existingColumns.Contains("IsDeleted");

                if (!hasText) return 0; // Can't import without text column

                // Build SELECT dynamically based on available columns
                string selectSql = hasColor
                    ? "SELECT Text, Color FROM Note"
                    : "SELECT Text FROM Note";

                // Exclude deleted notes if the column exists
                if (hasIsDeleted)
                    selectSql += " WHERE IsDeleted = 0 OR IsDeleted IS NULL";

                selectSql += ";";

                var cmd = conn.CreateCommand();
                cmd.CommandText = selectSql;

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        try
                        {
                            string rtfText = reader.IsDBNull(0) ? "" : reader.GetString(0);
                            string colorStr = (hasColor && !reader.IsDBNull(1))
                                ? reader.GetValue(1).ToString() ?? "yellow"
                                : "yellow";

                            if (string.IsNullOrWhiteSpace(rtfText)) continue;

                            // Strip internal Microsoft Sticky Notes metadata tags (\id=..., \np=..., etc.)
                            rtfText = StripStickyNotesMetadata(rtfText);

                            string xamlContent = ConvertRtfToXaml(rtfText);
                            string plainText = GetPlainTextFromXaml(xamlContent);
                            string title = "";

                            using (var readerStr = new StringReader(plainText))
                            {
                                title = readerStr.ReadLine() ?? "";
                            }
                            if (title.Length > 30) title = title.Substring(0, 30);
                            if (string.IsNullOrWhiteSpace(title)) title = "Imported Note";

                            string color = MapStickyNotesColor(colorStr);

                            CreateNote(title, xamlContent, null, null, color);
                            importCount++;
                        }
                        catch (Exception ex)
                        {
                            // Skip notes that fail individually rather than aborting the whole import
                            Console.WriteLine($"Skipping note during import: {ex.Message}");
                        }
                    }
                }
            }

            try
            {
                File.Delete(tempDb);
                if (File.Exists(tempDb + "-wal")) File.Delete(tempDb + "-wal");
                if (File.Exists(tempDb + "-shm")) File.Delete(tempDb + "-shm");
            }
            catch {}

            return importCount;
        }

        private static string StripStickyNotesMetadata(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            // Microsoft Sticky Notes stores internal metadata inline in the Text field
            // as \key=value pairs, e.g.: \id=04d88de6-2bef-4db9-a8d9-ad351625a2d3
            // Known keys: id, np, li, wi, ts, bidi, lnspc
            text = System.Text.RegularExpressions.Regex.Replace(
                text,
                @"\\(?:id|np|li|wi|ts|bidi|lnspc)=[^\s\\]*\s?",
                "",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return text.Trim();
        }

        private static string ConvertRtfToXaml(string rtf)
        {
            try
            {
                if (string.IsNullOrEmpty(rtf)) return "";
                
                if (!rtf.TrimStart().StartsWith("{\\rtf"))
                {
                    var p = new Paragraph(new Run(rtf));
                    var doc = new FlowDocument(p);
                    return SerializeFlowDocumentToXaml(doc);
                }

                var tempDoc = new FlowDocument();
                var range = new TextRange(tempDoc.ContentStart, tempDoc.ContentEnd);
                using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(rtf)))
                {
                    range.Load(ms, DataFormats.Rtf);
                }
                
                using (var msOut = new MemoryStream())
                {
                    var rangeOut = new TextRange(tempDoc.ContentStart, tempDoc.ContentEnd);
                    rangeOut.Save(msOut, DataFormats.Xaml);
                    return Encoding.UTF8.GetString(msOut.ToArray());
                }
            }
            catch
            {
                var p = new Paragraph(new Run(rtf));
                var doc = new FlowDocument(p);
                return SerializeFlowDocumentToXaml(doc);
            }
        }

        private static string SerializeFlowDocumentToXaml(FlowDocument doc)
        {
            using (var ms = new MemoryStream())
            {
                var range = new TextRange(doc.ContentStart, doc.ContentEnd);
                range.Save(ms, DataFormats.Xaml);
                return Encoding.UTF8.GetString(ms.ToArray());
            }
        }

        private static string GetPlainTextFromXaml(string xaml)
        {
            if (string.IsNullOrEmpty(xaml)) return "";
            try
            {
                string text = System.Text.RegularExpressions.Regex.Replace(xaml, "<[^>]+>", "");
                text = text.Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">").Replace("&quot;", "\"");
                return text.Trim();
            }
            catch
            {
                return xaml;
            }
        }

        private static string MapStickyNotesColor(string colorStr)
        {
            if (string.IsNullOrEmpty(colorStr)) return "yellow";
            colorStr = colorStr.ToLower();
            if (colorStr.Contains("yellow") || colorStr == "0") return "yellow";
            if (colorStr.Contains("green") || colorStr == "1") return "green";
            if (colorStr.Contains("pink") || colorStr.Contains("red") || colorStr == "2") return "pink";
            if (colorStr.Contains("purple") || colorStr == "3") return "purple";
            if (colorStr.Contains("blue") || colorStr == "4") return "blue";
            if (colorStr.Contains("charcoal") || colorStr.Contains("grey") || colorStr.Contains("gray") || colorStr == "5") return "charcoal";
            return "yellow";
        }
    }
}
