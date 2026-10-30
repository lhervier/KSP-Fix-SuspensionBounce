namespace com.github.lhervier.ksp.fix.suspensionbounce
{
    /// <summary>How much the mod writes to KSP.log, from the quietest to the most talkative.</summary>
    internal enum LogLevel
    {
        Error,
        Warning,
        Info,
        Debug
    }

    /// <summary>Writes to KSP.log, tagged with the mod's name, filtered by <see cref="Level"/>.</summary>
    internal static class Log
    {
        private const string Prefix = "[KSPFixSuspensionBounce] ";

        /// <summary>The level messages are filtered at.</summary>
        public static LogLevel Level { get; set; } = LogLevel.Info;

        public static bool IsDebugEnabled => Level >= LogLevel.Debug;

        public static void Error(string message)
        {
            UnityEngine.Debug.LogError(Prefix + message);
        }

        public static void Warning(string message)
        {
            if (Level >= LogLevel.Warning)
            {
                UnityEngine.Debug.LogWarning(Prefix + message);
            }
        }

        public static void Info(string message)
        {
            if (Level >= LogLevel.Info)
            {
                UnityEngine.Debug.Log(Prefix + message);
            }
        }

        public static void Debug(string message)
        {
            if (Level >= LogLevel.Debug)
            {
                UnityEngine.Debug.Log(Prefix + "[DEBUG] " + message);
            }
        }
    }
}
