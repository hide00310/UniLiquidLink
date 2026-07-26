using System.IO;
using System.Runtime.CompilerServices;

namespace LLiquidLink
{
    /// <summary>Small filesystem/reflection helpers shared across the core library.</summary>
    public static class Utils
    {
        /// <summary>Return the directory containing the caller's source file (via <see cref="CallerFilePathAttribute"/>).</summary>
        /// <param name="path">Supplied by the compiler; do not pass explicitly.</param>
        /// <returns>The directory of <paramref name="path"/>, or <c>null</c> if <paramref name="path"/> is empty.</returns>
        public static string GetCurrentDirectory([CallerFilePath] string path = null)
        {
            return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
        }
    }
}
