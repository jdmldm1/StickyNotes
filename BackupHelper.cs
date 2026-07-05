using System;
using System.IO;
using System.IO.Compression;

namespace StickyNotes__
{
    public static class BackupHelper
    {
        public static void CreateBackup(string destZipPath)
        {
            if (File.Exists(destZipPath)) File.Delete(destZipPath);

            string tempDir = Path.Combine(Path.GetTempPath(), "StickyNotesBackup_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                if (File.Exists(AppConfig.DbPath))
                    File.Copy(AppConfig.DbPath, Path.Combine(tempDir, "notes.db"), true);

                if (File.Exists(AppConfig.SettingsPath))
                    File.Copy(AppConfig.SettingsPath, Path.Combine(tempDir, "settings.json"), true);

                CopyDirectory(AppConfig.ImagesDir, Path.Combine(tempDir, "images"));
                CopyDirectory(AppConfig.AttachmentsDir, Path.Combine(tempDir, "attachments"));

                ZipFile.CreateFromDirectory(tempDir, destZipPath, CompressionLevel.Optimal, false);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        public static void RestoreBackup(string zipPath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "StickyNotesRestore_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            try
            {
                ZipFile.ExtractToDirectory(zipPath, tempDir, true);

                string dbSource = Path.Combine(tempDir, "notes.db");
                if (!File.Exists(dbSource))
                    throw new FileNotFoundException("The selected backup file does not contain a valid notes database.");

                File.Copy(dbSource, AppConfig.DbPath, true);

                string settingsSource = Path.Combine(tempDir, "settings.json");
                if (File.Exists(settingsSource))
                    File.Copy(settingsSource, AppConfig.SettingsPath, true);

                CopyDirectory(Path.Combine(tempDir, "images"), AppConfig.ImagesDir, clearDestFirst: true);
                CopyDirectory(Path.Combine(tempDir, "attachments"), AppConfig.AttachmentsDir, clearDestFirst: true);
            }
            finally
            {
                Directory.Delete(tempDir, true);
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir, bool clearDestFirst = false)
        {
            Directory.CreateDirectory(destDir);

            if (clearDestFirst)
            {
                foreach (var existing in Directory.GetFiles(destDir))
                {
                    try { File.Delete(existing); } catch { }
                }
            }

            if (!Directory.Exists(sourceDir)) return;

            foreach (var file in Directory.GetFiles(sourceDir))
            {
                File.Copy(file, Path.Combine(destDir, Path.GetFileName(file)), true);
            }
        }
    }
}
