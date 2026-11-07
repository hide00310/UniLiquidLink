using LLiquidLink;

namespace UniLiquidLink
{

    /// <summary>
    /// JSON converter for the base <see cref="UnityEngine.Object"/> type, via the shared registry-lookup logic in <see cref="InstanceObjectConverter{T}"/>.
    /// The injected <see cref="IRpcNullChecker{T}"/> (normally <see cref="UnityObjectNullChecker"/>) makes destroyed / fake-null
    /// objects (e.g. the result of <c>GetComponent</c> for a missing component) be written as JSON null instead of an object descriptor.
    /// </summary>
    public class UnityObjectConverter : InstanceObjectConverter<UnityEngine.Object>
    {
        /// <summary>Initialize the converter with the registry used for instance ID lookups and the null definition to apply.</summary>
        /// <param name="registry">Registry mapping instance IDs to live Unity objects.</param>
        /// <param name="nullChecker">Decides which Unity objects are written as JSON null; required because Unity's null-ness is not reference equality.</param>
        public UnityObjectConverter(ObjectRegistry registry, IRpcNullChecker<UnityEngine.Object> nullChecker) : base(registry, nullChecker)
        {
        }
    }
}
