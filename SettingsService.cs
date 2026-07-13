using System;
using System.IO;
using System.Text.Json;

namespace StickyNotes__
{
    public static class SettingsService
    {
        private static AppConfigData? _cache;
        private static readonly object _lock = new();

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

        public static void Invalidate() => _cache = null;

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
