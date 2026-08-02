using System.Collections;
using System.Text;

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

    /// <summary>Wraps an <see cref="IEnumerable"/> so its elements are rendered in log output instead of the array's type name.</summary>
    public class ArrayLogFormatter
    {
        readonly IEnumerable _items;

        /// <summary>Initialize the formatter with the collection to render.</summary>
        /// <param name="items">Collection whose elements will be stringified.</param>
        public ArrayLogFormatter(IEnumerable items)
        {
            _items = items;
        }

        /// <summary>Render the collection as <c>[item1, item2, ...]</c>, using <c>"null"</c> for null elements. Returns <c>"null"</c> if the collection itself is null.</summary>
        /// <returns>The formatted string.</returns>
        public override string ToString()
        {
            if (_items == null)
            {
                return "null";
            }

            var sb = new StringBuilder();
            sb.Append('[');
            bool first = true;
            foreach (object item in _items)
            {
                if (!first)
                {
                    sb.Append(", ");
                }
                first = false;
                sb.Append(item == null ? "null" : item.ToString());
            }
            sb.Append(']');
            return sb.ToString();
        }
    }
}
