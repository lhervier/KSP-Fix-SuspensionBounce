using System;
using System.IO;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>How the mod sizes the damper of each suspension (landing leg, wheel, landing gear).</summary>
    internal enum DamperMode
    {
        /// <summary>The mod changes nothing: every damper is the one KSP sets.</summary>
        Stock,

        /// <summary>
        /// The damper KSP sets is kept, unless it is stiffer than the mass its suspension carries can take in one
        /// physics step without bouncing back: it is then lowered to that limit. Only very light vessels and parts
        /// placed alone reach it.
        /// </summary>
        Minimal,

        /// <summary>
        /// Every damper is sized from both the spring and the mass its suspension carries, so that all vessels settle
        /// on their legs the same way, whatever their mass; the limit of <see cref="Minimal"/> still applies.
        /// </summary>
        MassBased
    }

    /// <summary>
    /// What the player can set in PluginData/settings.cfg, next to the DLL. Read once, when KSP starts.
    /// </summary>
    internal static class Settings
    {
        /// <summary>How the dampers are sized (see <see cref="DamperMode"/>).</summary>
        public static DamperMode Mode { get; private set; } = DamperMode.Minimal;

        /// <summary>
        /// Reads the settings file and applies its log level. A missing file, a missing value or an unknown
        /// value leaves the default: the Minimal mode, logging at Info.
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
            Mode = ReadEnum(node, "mode", Mode, path);
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
