using Frosty.Core;

namespace MeshSetExtender.Core
{
    /// <summary>
    /// Simple logging wrapper for the Cloth Generator plugin.
    ///
    /// - Log() always prints (key results, errors, summary info)
    /// - LogDebug() only prints when DebugMode is enabled (verbose internals)
    /// </summary>
    public static class ClothLogger
    {
        /// <summary>
        /// When true, verbose debug messages are printed to Frosty's log.
        /// Controlled by the Debug checkbox in the generator window.
        /// </summary>
        public static bool DebugMode { get; set; } = false;

        private const string Prefix = " ";

        /// <summary>
        /// Always logs to Frosty's output. Use for key results and summaries.
        /// </summary>
        public static void Log(string message)
        {
            App.Logger.Log(Prefix + message);
        }

        /// <summary>
        /// Only logs when DebugMode is enabled. Use for verbose internals.
        /// </summary>
        public static void LogDebug(string message)
        {
            if (DebugMode)
                App.Logger.Log(Prefix + message);
        }

        /// <summary>
        /// Always logs a warning.
        /// </summary>
        public static void LogWarning(string message)
        {
            App.Logger.LogWarning(Prefix + message);
        }

        /// <summary>
        /// Always logs an error.
        /// </summary>
        public static void LogError(string message)
        {
            App.Logger.LogError(Prefix + message);
        }
    }
}
