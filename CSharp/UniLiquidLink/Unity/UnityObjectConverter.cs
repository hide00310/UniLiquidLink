using LLiquidLink;

namespace UniLiquidLink
{
    public class UnityObjectConverter : InstanceObjectConverter<UnityEngine.Object>
    {
        public UnityObjectConverter(ObjectRegistry registry) : base(registry) { }
    }
}
