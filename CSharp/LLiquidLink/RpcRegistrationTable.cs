using System;
using System.Collections.Generic;

namespace LLiquidLink
{
    /// <summary>
    /// Shared registration data written by <see cref="RpcRegistry"/> and read by <see cref="RpcSearcher"/>.
    /// Not exposed outside the assembly; access goes through the owning registrar/searcher instead.
    /// </summary>
    internal class RpcRegistrationTable
    {
        /// <summary>Maps a wire-side <c>rpcType</c> name to its registered original .NET type.</summary>
        public readonly Dictionary<string, Type> RpcTypeToOrgType = new Dictionary<string, Type>();

        /// <summary>Registered property getters, keyed by (owning type, property name); <c>null</c> type means root-level.</summary>
        public readonly Dictionary<(Type ObjType, string PropertyName), (Type PropertyType, Delegate Method)> RpcProperties
            = new Dictionary<(Type, string), (Type, Delegate)>();

        /// <summary>Registered property setters, keyed by (owning type, property name).</summary>
        public readonly Dictionary<(Type ObjType, string PropertyName), (Type PropertyType, Action<object, object> Setter)> RpcSetProperties
            = new Dictionary<(Type, string), (Type, Action<object, object>)>();

        /// <summary>RPC method overload candidates, keyed by RPC name.</summary>
        public readonly Dictionary<string, IList<RpcSearcher.MethodCandidate>> Router
            = new Dictionary<string, IList<RpcSearcher.MethodCandidate>>();

        /// <summary>Direct-dispatch overload candidates, keyed by (instance type, method name).</summary>
        public readonly Dictionary<(Type InstanceType, string MethodName), List<RpcSearcher.MethodCandidate>> DirectEntries
            = new Dictionary<(Type, string), List<RpcSearcher.MethodCandidate>>();
    }
}
