using System.IO;
using System.Text.Json;

namespace IrrModWorkbench.Services
{
    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(AppContext.BaseDirectory, "directories.json");
        private static Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);

        static SettingsService()
        {
            Load();
        }

        public static string? Get(string key)
        {
            return _values.TryGetValue(key, out var value) ? value : null;
        }

        public static string? GetValidPath(string key)
        {
            var value = Get(key);
            if (string.IsNullOrWhiteSpace(value)) return null;
            return Directory.Exists(value) || File.Exists(value) ? value : null;
        }

        public static void Set(string key, string value)
        {
            _values[key] = value;
            Save();
        }

        private static void Load()
        {
            try
            {
                if (!File.Exists(SettingsPath))
                    return;

                var text = File.ReadAllText(SettingsPath);
                if (string.IsNullOrWhiteSpace(text))
                    return;

                var loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(text);
                if (loaded != null)
                    _values = new Dictionary<string, string>(loaded, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch
            {
                // Best effort; ignore save failures.
            }
        }
    }
}