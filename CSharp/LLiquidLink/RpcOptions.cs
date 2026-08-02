namespace LLiquidLink
{
    /// <summary>Options controlling bulk registration (AddRpcAll*) member enumeration.</summary>
    public class RpcOptions
    {
        /// <summary>When <c>true</c>, include members inherited from base types (except those declared on <see cref="object"/>).</summary>
        public bool IncludeInherited { get; set; }

        /// <summary>When <c>true</c>, recurse into public nested types as well as the type itself.</summary>
        public bool IncludeNested { get; set; }
    }
}
