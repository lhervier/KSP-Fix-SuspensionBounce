using System;
using System.IO;

namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>
    /// What the player can set in PluginData/settings.cfg, next to the DLL. Read once, when KSP starts.
    /// </summary>
    internal static class Settings
    {
        /// <summary>
        /// Reads the settings file and applies its log level. A missing file, a missing value or an unknown
        /// value leaves the default: logging at Info.
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
            BumpStopLoad.Enabled = ReadBool(node, "bumpStopLoad", BumpStopLoad.Enabled, path);
            SpringGravityCurve.Enabled = ReadBool(node, "kerbinCurve", SpringGravityCurve.Enabled, path);
            SuspensionDamper.OnGround = ReadBool(node, "damperLimit", SuspensionDamper.OnGround, path);
            SuspensionDamper.InAir = ReadBool(node, "airDamper", SuspensionDamper.InAir, path);
            TyreStaticGrip.Enabled = ReadBool(node, "staticGrip", TyreStaticGrip.Enabled, path);
            PoseMotion.Enabled = ReadBool(node, "tyreReadsPoses", PoseMotion.Enabled, path);
            CostMeasure.Enabled = ReadBool(node, "measureCost", CostMeasure.Enabled, path);
            LegGeometryProbe.Enabled = ReadBool(node, "probeLegGeometry", LegGeometryProbe.Enabled, path);
            FixedTuning.Enabled = ReadBool(node, "fixedTuning", FixedTuning.Enabled, path);
            FixedTuning.Compression = ReadFloat(node, "fixedTuningCompression", FixedTuning.Compression, path);
            FixedTuning.Damping = ReadFloat(node, "fixedTuningDamping", FixedTuning.Damping, path);
            FixedTuning.TipMargin = ReadFloat(node, "fixedTuningTipMargin", FixedTuning.TipMargin, path);
            FixedTuning.Wheels = node.GetValue("fixedTuningWheels") ?? FixedTuning.Wheels;
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
            if (bool.TryParse(value, out parsed))
            {
                return parsed;
            }
            Log.Warning($"Unknown {name} '{value}' in {path}, using {fallback}");
            return fallback;
        }

        /// <summary>
        /// Returns the value <paramref name="name"/> of <paramref name="node"/> as a number, or
        /// <paramref name="fallback"/> when it is missing or not a number (the latter with a warning).
        /// </summary>
        private static float ReadFloat(ConfigNode node, string name, float fallback, string path)
        {
            string value = node.GetValue(name);
            if (value == null)
            {
                return fallback;
            }
            float parsed;
            if (float.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out parsed))
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
