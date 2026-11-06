namespace LLiquidLink
{
    /// <summary>
    /// Accumulator for one RPC call's <see cref="MethodCaller.Call(string, System.Text.Json.JsonElement[], RpcStageTimings)"/>
    /// stage breakdown, in seconds. Passed in by the caller (e.g. <see cref="StdioTransport"/>) and read back
    /// afterward; <see cref="MethodCaller"/> only ever writes to it, never reads it.
    /// </summary>
    public class RpcStageTimings
    {
        /// <summary>Time spent resolving the RPC method name to overload candidates.</summary>
        public double ResolveTime;

        /// <summary>Time spent deserializing JSON arguments into call arguments, including failed overload attempts.</summary>
        public double DeserializeArgsTime;

        /// <summary>Time spent inside the resolved method itself (pure handler execution).</summary>
        public double InvokeTime;

        /// <summary>Time spent serializing the method's return value to a <see cref="System.Text.Json.JsonElement"/>.</summary>
        public double SerializeResultTime;

        /// <summary>Number of overload candidates whose argument deserialization was attempted.</summary>
        public int OverloadAttempts;
    }
}
