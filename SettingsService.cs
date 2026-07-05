using System;
using System.IO;
using System.Text.Json;

namespace StickyNotes__
{
    /// <summary>
    /// Singleton settings cache. Reads settings.json once and exposes typed properties.
    /// Call Invalidate() after saving settings to force a reload on next access.
    /// </summary>
    public static class SettingsService
    {
        private static AppConfigData? _cache;
        private static readonly object _lock = new();

        /// <summary>Gets the current cached settings, loading from disk if not yet cached.</summary>
        public static AppConfigData Current
        {
            get
            {
                if (_cache != null) return _cache;
                lock (_lock)
                {
                    _cache ??= Load();
                    return _cache;
                }
            }
        }

        /// <summary>Clears the cache so the next access re-reads from disk.</summary>
        public static void Invalidate() => _cache = null;

        /// <summary>Saves the given config to disk and invalidates the cache.</summary>
        public static void Save(AppConfigData config)
        {
            try
            {
                Directory.CreateDirectory(AppConfig.AppDir);
                string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(AppConfig.SettingsPath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("SettingsService.Save error: " + ex.Message);
            }
            finally
            {
                Invalidate();
            }
        }

        private static AppConfigData Load()
        {
            try
            {
                if (File.Exists(AppConfig.SettingsPath))
                {
                    string json = File.ReadAllText(AppConfig.SettingsPath);
                    return JsonSerializer.Deserialize<AppConfigData>(json) ?? new AppConfigData();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("SettingsService.Load error: " + ex.Message);
            }
            return new AppConfigData();
        }
    }
}
