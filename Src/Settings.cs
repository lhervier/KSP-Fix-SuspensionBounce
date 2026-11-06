using System;
using System.IO;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// What the player can set in PluginData/settings.cfg, next to the DLL. Read once, when KSP starts.
    /// </summary>
    internal static class Settings
    {
        // Fields, not properties, so that a test can switch them while KSP runs.

        /// <summary>Whether the gravity curve of the spring's boost is held at its value on Kerbin below 1 g.</summary>
        public static bool KerbinCurve = true;

        /// <summary>Whether the spring of a wheel that carries more than its share is sized on its load.</summary>
        public static bool LoadSpring = true;

        /// <summary>
        /// Reads the settings file and applies its log level. A missing file, a missing value or an unknown
        /// value leaves the default: logging at Info, both spring fixes on.
        /// </summary>
        public static void Load()
        {
            string folder = Path.GetDirectoryName(typeof(Settings).Assembly.Location);
            string path = Path.Combine(Path.Combine(folder, "PluginData"), "settings.cfg");
            if (!File.Exists(path))
            {
                Log.Warning($"No settings file at {path}, using the defaults");
                return;
            }
            ConfigNode node = ConfigNode.Load(path);
            if (node == null)
            {
                Log.Warning($"Could not read {path}, using the defaults");
                return;
            }

            Log.Level = ReadEnum(node, "logLevel", Log.Level, path);
            KerbinCurve = ReadBool(node, "kerbinCurve", KerbinCurve, path);
            LoadSpring = ReadBool(node, "loadSpring", LoadSpring, path);
        }

        /// <summary>
        /// Returns the value <paramref name="name"/> of <paramref name="node"/> as a boolean, or
        /// <paramref name="fallback"/> when it is missing or not a boolean (the latter with a warning).
        /// </summary>
        private static bool ReadBool(ConfigNode node, string name, bool fallback, string path)
        {
            string value = node.GetValue(name);
            if (value == null)
            {
                return fallback;
            }
            bool parsed;
            if (bool.TryParse(value.Trim(), out parsed))
            {
                return parsed;
            }
            Log.Warning($"Unknown {name} '{value}' in {path}, using {fallback}");
            return fallback;
        }

        /// <summary>
        /// Returns the value <paramref name="name"/> of <paramref name="node"/> as a member of
        /// <typeparamref name="T"/>, case ignored, or <paramref name="fallback"/> when it is missing or unknown (the
        /// latter with a warning).
        /// </summary>
        private static T ReadEnum<T>(ConfigNode node, string name, T fallback, string path) where T : struct
        {
            string value = node.GetValue(name);
            if (value == null)
            {
                return fallback;
            }
            T parsed;
            if (Enum.TryParse(value, true, out parsed) && Enum.IsDefined(typeof(T), parsed))
            {
                return parsed;
            }
            Log.Warning($"Unknown {name} '{value}' in {path}, using {fallback}");
            return fallback;
        }
    }
}
