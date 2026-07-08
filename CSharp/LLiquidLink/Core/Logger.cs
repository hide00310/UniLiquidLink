namespace LLiquidLink.Logger
{

    /// <summary>Severity levels for the built-in logger.</summary>
    public enum LogLevel { Debug = 0, Info = 1, None = 2 }

    /// <summary>Logging interface for Server diagnostic output.</summary>
    public interface ILogger
    {
        /// <summary>Minimum severity level; messages below this level are suppressed.</summary>
        LogLevel MinLevel { get; set; }

        /// <summary>Log an informational message.</summary>
        /// <param name="msg">Message text.</param>
        void Info(string msg);

        /// <summary>Log a debug-level message.</summary>
        /// <param name="msg">Message text.</param>
        void Debug(string msg);

        /// <summary>Log a formatted informational message.</summary>
        /// <param name="format">Format string.</param>
        /// <param name="args">Format arguments.</param>
        void InfoFormat(string format, params object[] args);

        /// <summary>Log a formatted debug-level message.</summary>
        /// <param name="format">Format string.</param>
        /// <param name="args">Format arguments.</param>
        void DebugFormat(string format, params object[] args);
    }
}
