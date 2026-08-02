using LLiquidLink;

namespace UniLiquidLink
{

    /// <summary>JSON converter for the base <see cref="UnityEngine.Object"/> type, via the shared registry-lookup logic in <see cref="InstanceObjectConverter{T}"/>.</summary>
    public class UnityObjectConverter : InstanceObjectConverter<UnityEngine.Object>
    {
        /// <summary>Initialize the converter with the registry used for instance ID lookups.</summary>
        /// <param name="registry">Registry mapping instance IDs to live Unity objects.</param>
        public UnityObjectConverter(ObjectRegistry registry) : base(registry)
        {
        }
    }
}
