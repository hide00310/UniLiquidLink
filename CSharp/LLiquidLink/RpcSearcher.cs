using System;
using System.Collections.Generic;
using System.Reflection;

namespace LLiquidLink
{
    /// <summary>
    /// Read-only lookup over the registration data written by <see cref="RpcRegistry"/>: resolves RPC
    /// method overloads, property accessors, and wire-type mappings for <see cref="MethodCaller"/> dispatch.
    /// </summary>
    public class RpcSearcher
    {
        /// <summary>Describes one overload candidate of an RPC method: how to build its arguments and invoke it.</summary>
        public class MethodCandidate
        {
            /// <summary>When <c>true</c>, all JSON arguments map to <see cref="MethodParams"/> and no instance argument is consumed.</summary>
            public bool IsStatic;

            /// <summary>For instance methods, the type used to deserialize the first JSON argument (the instance).</summary>
            public Type InstanceType;

            /// <summary>Parameter descriptors of the target method (excluding the instance parameter).</summary>
            public ParameterInfo[] MethodParams;

            /// <summary>Invocation body: receives <c>[arg0, ...]</c> for static methods or <c>[instance, arg0, ...]</c> for instance methods.</summary>
            public Func<object[], object> Method;

            /// <summary>Fully qualified method name for logging.</summary>
            public string FullName;
        }

        readonly RpcRegistrationTable _table;
        readonly TypeResolver _typeResolver;

        /// <summary>Initialize a searcher bound to a registration table and type resolver. Constructed once by <see cref="RpcRegistry"/>.</summary>
        /// <param name="table">Registration table populated by the owning <see cref="RpcRegistry"/>.</param>
        /// <param name="typeResolver">Resolver used to turn a root object's <c>orgType</c> name into its concrete <see cref="Type"/>.</param>
        internal RpcSearcher(RpcRegistrationTable table, TypeResolver typeResolver)
        {
            _table = table;
            _typeResolver = typeResolver;
        }

        /// <summary>Look up the overload candidates registered under <paramref name="rpcName"/>.</summary>
        public bool TryGetCandidates(string rpcName, out IList<MethodCandidate> candidates)
        {
            return _table.Router.TryGetValue(rpcName, out candidates);
        }

        /// <summary>Look up the direct-dispatch overload candidates for <paramref name="methodName"/> on <paramref name="instanceType"/>.</summary>
        public bool TryGetDirectCandidates(Type instanceType, string methodName, out List<MethodCandidate> candidates)
        {
            return _table.DirectEntries.TryGetValue((instanceType, methodName), out candidates);
        }

        /// <summary>Look up a root-level (null-obj) property getter by name.</summary>
        public bool TryGetRootProperty(string name, out (Type PropertyType, Delegate Method) property)
        {
            return _table.RpcProperties.TryGetValue((null, name), out property);
        }

        /// <summary>Look up a property getter for <paramref name="t"/> by name.</summary>
        public bool TryGetProperty(Type t, string name, out (Type PropertyType, Delegate Method) property)
        {
            return _table.RpcProperties.TryGetValue((t, name), out property);
        }

        /// <summary>Look up a property setter for <paramref name="t"/> by name.</summary>
        public bool TryGetSetProperty(Type t, string name, out (Type PropertyType, Action<object, object> Setter) property)
        {
            return _table.RpcSetProperties.TryGetValue((t, name), out property);
        }

        /// <summary>Resolve a wire-side <c>rpcType</c> name to its registered original .NET type.</summary>
        public bool TryResolveRpcType(string rpcTypeName, out Type orgType)
        {
            return _table.RpcTypeToOrgType.TryGetValue(rpcTypeName, out orgType);
        }

        /// <summary>Resolve a concrete .NET type name via the configured <see cref="TypeResolver"/>.</summary>
        public Type ResolveOrgType(string typeName)
        {
            return _typeResolver.Resolve(typeName);
        }
    }
}
