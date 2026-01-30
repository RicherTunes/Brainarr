using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using Brainarr.Plugin.Services.Security;

namespace NzbDrone.Core.ImportLists.Brainarr.Services.Providers.Shared
{
    public static class FormatPreferenceCache
    {
        // Caches whether to prefer structured JSON for a given provider key
        private static readonly ConcurrentDictionary<string, bool> _preferStructured = new ConcurrentDictionary<string, bool>();
        private static bool _loaded = false;
        private static readonly object _sync = new object();

        public static bool GetPreferStructuredOrDefault(string key, bool @default)
        {
            EnsureLoaded();
            if (string.IsNullOrWhiteSpace(key)) return @default;
            return _preferStructured.TryGetValue(key, out var val) ? val : @default;
        }

        public static void SetPreferStructured(string key, bool prefer)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            _preferStructured[key] = prefer;
            Save();
        }

        /// <summary>
        /// Clears all cached preferences. Intended for test isolation.
        /// </summary>
        public static void Clear()
        {
            lock (_sync)
            {
                _preferStructured.Clear();
                _loaded = false;
            }
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            lock (_sync)
            {
                if (_loaded) return;
                try
                {
                    var (dir, path) = GetPath();
                    if (File.Exists(path))
                    {
                        var json = File.ReadAllText(path);
                        var dict = SecureJsonSerializer.Deserialize<Dictionary<string, bool>>(json);
                        if (dict != null)
                        {
                            foreach (var kv in dict)
                            {
                                _preferStructured[kv.Key] = kv.Value;
                            }
                        }
                    }
                }
                catch (Exception) { /* Non-critical */ }
                finally { _loaded = true; }
            }
        }

        private static void Save()
        {
            try
            {
                var (dir, path) = GetPath();
                Directory.CreateDirectory(dir);
                var dict = new Dictionary<string, bool>(_preferStructured);
                var json = SecureJsonSerializer.Serialize(dict);
                File.WriteAllText(path, json);
            }
            catch (Exception) { /* Non-critical */ }
        }

        private static (string dir, string path) GetPath()
        {
            // Prefer an override directory if provided (useful for services/containers/tests)
            try
            {
                var overrideDir = Environment.GetEnvironmentVariable("BRAINARR_PREFS_DIR");
                if (!string.IsNullOrWhiteSpace(overrideDir))
                {
                    var pathOverride = Path.Combine(overrideDir, "format-preferences.json");
                    return (overrideDir, pathOverride);
                }
            }
            catch (Exception) { /* Non-critical */ }

            // Default to per-user AppData
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var dir = Path.Combine(appData, "Brainarr", "Prefs");
            var path = Path.Combine(dir, "format-preferences.json");
            return (dir, path);
        }
    }
}
