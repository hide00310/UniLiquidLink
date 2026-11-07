using LLiquidLink;

namespace UniLiquidLink
{

    /// <summary>
    /// Null checker for <see cref="UnityEngine.Object"/> values, meant to be injected into <see cref="UnityObjectConverter"/>.
    /// Uses Unity's overloaded <c>==</c>, so destroyed objects and the "fake null" returned by e.g.
    /// <c>GetComponent</c> for a missing component count as null although they are not reference-null.
    /// </summary>
    public sealed class UnityObjectNullChecker : IRpcNullChecker<UnityEngine.Object>
    {
        /// <summary>Whether <paramref name="value"/> is a destroyed or fake-null Unity object.</summary>
        /// <param name="value">Value about to be written.</param>
        /// <returns><c>true</c> if Unity considers the object null.</returns>
        public bool IsNull(UnityEngine.Object value)
        {
            return value == null;
        }
    }
}
