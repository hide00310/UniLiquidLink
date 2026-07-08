using LLiquidLink.Logger;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;

namespace LLiquidLink
{
    /// <summary>Routes incoming JSON-RPC calls to registered delegate or direct-method handlers.</summary>
    public class RpcBus
    {
        readonly Func<ILogger> _getLogger;
        readonly JsonSerializerChain _chain;

        readonly Dictionary<string, Func<JsonElement[], object>> _router
            = new Dictionary<string, Func<JsonElement[], object>>();

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
            public Func<object[], object> Body;

            /// <summary>Fully qualified method name for logging.</summary>
            public string FullName;
        }

        readonly Dictionary<(Type InstanceType, string MethodName), List<MethodCandidate>> _directEntries
            = new Dictionary<(Type, string), List<MethodCandidate>>();

        /// <summary>
        /// Additional attribute types (beyond <see cref="DefaultValueAttribute"/>) that expose a public
        /// <c>Value</c> property, checked via reflection when resolving omitted RPC parameter defaults.
        /// Unity integrations (e.g. <c>UnityEngine.Internal.DefaultValueAttribute</c>) register their
        /// attribute type here at startup so this class never references UnityEngine directly.
        /// Only ever written once from a static constructor before any dispatch runs, so concurrent
        /// writes are not a concern.
        /// </summary>
        public static readonly HashSet<Type> AdditionalDefaultValueAttributeTypes = new HashSet<Type>();

        /// <summary>Initialize the bus with shared serialization infrastructure.</summary>
        /// <param name="getLogger">Factory that returns the current logger.</param>
        /// <param name="chain">Pre/main/fallback JSON serializer stage chain.</param>
        public RpcBus(Func<ILogger> getLogger, JsonSerializerChain chain)
        {
            _getLogger = getLogger;
            _chain = chain;
        }

        /// <summary>Names of all registered RPC handlers.</summary>
        public IEnumerable<string> RegisteredRpcNames => _router.Keys;

        /// <summary>
        /// Register a delegate under <paramref name="rpcName"/>. Arguments are deserialized from JSON
        /// using the delegate's parameter types.
        /// </summary>
        /// <param name="rpcName">RPC method name clients use to call this handler.</param>
        /// <param name="method">Delegate to invoke.</param>
        public void Register(string rpcName, Delegate method)
        {
            ParameterInfo[] paramInfos = method.Method.GetParameters();
            string logName = (method.Method.DeclaringType != null ? method.Method.DeclaringType.FullName : "") + "." + method.Method.Name;
            _getLogger().DebugFormat("Register: {0} -> {1}", rpcName, logName);

            _router[rpcName] = (args) =>
            {
                object[] callArgs = DeserializeArgs(args, paramInfos);
                _getLogger().DebugFormat("CallMethod {0}", logName);
                return Invoke(method, callArgs);
            };
        }

        /// <summary>
        /// Register a method for direct instance dispatch. The first JSON argument is deserialized as the
        /// instance; remaining arguments are matched to <paramref name="methodParams"/>.
        /// </summary>
        /// <param name="rpcName">RPC method name clients use to call this handler.</param>
        /// <param name="instanceType">Expected type of the first (instance) argument.</param>
        /// <param name="methodName">Method name used as a key for chain resolution.</param>
        /// <param name="methodParams">Parameter descriptors for the method (excluding the instance).</param>
        /// <param name="body">Invocation body that receives <c>[instance, arg0, ...]</c>.</param>
        /// <param name="logName">Fully qualified name used for debug logging.</param>
        public void RegisterDirect(string rpcName, Type instanceType, string methodName, ParameterInfo[] methodParams, Func<object[], object> body, string logName)
        {
            var candidate = new MethodCandidate
            {
                IsStatic = false,
                InstanceType = instanceType,
                MethodParams = methodParams,
                Body = body,
                FullName = logName,
            };
            RegisterDirectMethodOverloads(rpcName, methodName, new List<MethodCandidate> { candidate });
        }

        /// <summary>
        /// Register one or more overload candidates under <paramref name="rpcName"/>. On dispatch, each candidate is
        /// tried in order and the first whose arguments deserialize successfully is invoked.
        /// </summary>
        /// <param name="rpcName">RPC method name clients use to call this handler.</param>
        /// <param name="candidates">Overload candidates, tried in order.</param>
        public void RegisterMethodOverloads(string rpcName, IList<MethodCandidate> candidates)
        {
            _getLogger().DebugFormat("Register: {0} ({1} overloads)", rpcName, candidates.Count);
            _router[rpcName] = (args) => DispatchCandidates(rpcName, candidates, args);
        }

        /// <summary>
        /// Register direct-dispatch overload candidates. Like <see cref="RegisterMethodOverloads"/>, but instance
        /// candidates are also indexed by <paramref name="methodName"/> so they can be reached via chain resolution.
        /// </summary>
        /// <param name="rpcName">RPC method name clients use to call this handler (typically prefixed with <c>"_"</c>).</param>
        /// <param name="methodName">Method name used as the chain-resolution key.</param>
        /// <param name="candidates">Overload candidates, tried in order.</param>
        public void RegisterDirectMethodOverloads(string rpcName, string methodName, IList<MethodCandidate> candidates)
        {
            _getLogger().DebugFormat("RegisterDirect: {0} -> {1} ({2} overloads)", rpcName, methodName, candidates.Count);

            foreach (MethodCandidate c in candidates)
            {
                if (c.IsStatic)
                {
                    continue;
                }
                var key = (c.InstanceType, methodName);
                if (!_directEntries.TryGetValue(key, out List<MethodCandidate> list))
                {
                    list = new List<MethodCandidate>();
                    _directEntries[key] = list;
                }
                list.Add(c);
            }

            _router[rpcName] = (args) => DispatchCandidates(rpcName, candidates, args);
        }

        /// <summary>Build the call-argument array for a single overload candidate, throwing if the JSON arguments don't fit.</summary>
        /// <param name="c">Overload candidate.</param>
        /// <param name="args">JSON argument elements.</param>
        /// <returns>Argument array ready for <see cref="MethodCandidate.Body"/>.</returns>
        object[] BuildCandidateArgs(MethodCandidate c, JsonElement[] args)
        {
            if (c.IsStatic)
            {
                return DeserializeArgs(args, c.MethodParams);
            }
            if (args.Length == 0)
            {
                throw new ArgumentException("Missing instance argument");
            }
            object instance = DeserializeArg(args[0], c.InstanceType);
            var restArgs = new JsonElement[args.Length - 1];
            Array.Copy(args, 1, restArgs, 0, restArgs.Length);
            return BuildInstanceCallArgs(instance, restArgs, c.MethodParams);
        }

        /// <summary>Try each overload candidate in order; invoke the first whose arguments deserialize successfully.</summary>
        /// <param name="rpcName">RPC method name (for error messages).</param>
        /// <param name="candidates">Overload candidates to try.</param>
        /// <param name="args">JSON argument elements.</param>
        /// <returns>Wrapped result of the first matching candidate.</returns>
        object DispatchCandidates(string rpcName, IList<MethodCandidate> candidates, JsonElement[] args)
        {
            Exception lastError = null;
            foreach (MethodCandidate c in candidates)
            {
                object[] callArgs;
                try
                {
                    callArgs = BuildCandidateArgs(c, args);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    continue;
                }
                _getLogger().DebugFormat("CallMethod {0}", c.FullName);
                return WrapResult(c.Body(callArgs));
            }
            if (lastError != null)
            {
                throw lastError;
            }
            throw new KeyNotFoundException("No matching overload for " + rpcName);
        }

        /// <summary>
        /// Invoke a directly-registered method on a pre-deserialized instance.
        /// Called from chain resolution on the main thread.
        /// </summary>
        /// <param name="orgObj">Already-deserialized instance object.</param>
        /// <param name="methodName">Name of the method to invoke.</param>
        /// <param name="restArgs">JSON argument elements for the method parameters.</param>
        /// <returns>Wrapped result, serialized to a <see cref="JsonElement"/>.</returns>
        public object DispatchDirectWithObj(object orgObj, string methodName, JsonElement[] restArgs)
        {
            List<MethodCandidate> candidates = null;
            for (Type t = orgObj.GetType(); t != null; t = t.BaseType)
            {
                if (_directEntries.TryGetValue((t, methodName), out candidates))
                {
                    break;
                }
            }
            if (candidates == null)
            {
                throw new KeyNotFoundException(string.Format(
                    "Direct method '{0}' not found for type '{1}'", methodName, orgObj.GetType()));
            }

            Exception lastError = null;
            foreach (MethodCandidate c in candidates)
            {
                object[] callArgs;
                try
                {
                    callArgs = BuildInstanceCallArgs(orgObj, restArgs, c.MethodParams);
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    continue;
                }
                _getLogger().DebugFormat("CallMethod {0}({1})", c.FullName, orgObj);
                return c.Body(callArgs);
            }
            throw lastError ?? new KeyNotFoundException(string.Format(
                "No matching direct overload '{0}' for type '{1}'", methodName, orgObj.GetType()));
        }

        /// <summary>Dispatch a registered RPC method by name with the given JSON arguments.</summary>
        /// <param name="method">RPC method name.</param>
        /// <param name="args">JSON argument elements.</param>
        /// <returns>Result wrapped as a <see cref="JsonElement"/>.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the method name is not registered.</exception>
        public object Dispatch(string method, JsonElement[] args)
        {
            return !_router.TryGetValue(method, out Func<JsonElement[], object> handler) ? throw new KeyNotFoundException("Method not found: " + method) : handler(args);
        }

        /// <summary>Build the full argument array for an instance method call, filling defaults for omitted params.</summary>
        /// <param name="instance">Deserialized instance object (placed at index 0).</param>
        /// <param name="restArgs">JSON elements for the non-instance parameters.</param>
        /// <param name="paramInfos">Parameter descriptors of the method (excluding instance).</param>
        /// <returns>Argument array <c>[instance, arg0, arg1, ...]</c>.</returns>
        object[] BuildInstanceCallArgs(object instance, JsonElement[] restArgs, ParameterInfo[] paramInfos)
        {
            object[] rest = DeserializeArgs(restArgs, paramInfos);
            var callArgs = new object[1 + rest.Length];
            callArgs[0] = instance;
            Array.Copy(rest, 0, callArgs, 1, rest.Length);
            return callArgs;
        }

        /// <summary>Deserialize a single JSON element to <paramref name="orgType"/> using the pre/main/fallback chain.</summary>
        /// <param name="arg">JSON element to deserialize.</param>
        /// <param name="orgType">Target .NET type.</param>
        /// <returns>Deserialized object.</returns>
        object DeserializeArg(JsonElement arg, Type orgType)
        {
            string rawJson = arg.GetRawText();
            object ret = _chain.Deserialize(rawJson, orgType);
            _getLogger().DebugFormat("DeserializeArg {0}->{1}", rawJson, ret);
            return ret;
        }

        /// <summary>Deserialize a JSON argument array, filling defaults for omitted trailing parameters.</summary>
        /// <param name="args">JSON argument elements.</param>
        /// <param name="paramInfos">Parameter descriptors of the target method.</param>
        /// <returns>Deserialized argument array aligned with <paramref name="paramInfos"/>.</returns>
        object[] DeserializeArgs(JsonElement[] args, ParameterInfo[] paramInfos)
        {
            if (args.Length > paramInfos.Length)
            {
                throw new ArgumentException(string.Format("Too many arguments: expected {0}, got {1}", paramInfos.Length, args.Length));
            }

            var result = new object[paramInfos.Length];
            for (int i = 0; i < paramInfos.Length; i++)
            {
                if (i >= args.Length)
                {
                    if (!TryGetDefaultValue(paramInfos[i], out object defaultVal))
                    {
                        throw new ArgumentException("Missing required argument: " + paramInfos[i].Name);
                    }

                    result[i] = defaultVal;
                }
                else
                {
                    result[i] = DeserializeArg(args[i], paramInfos[i].ParameterType);
                }
            }
            return result;
        }

        /// <summary>Invoke a delegate and wrap the result as a serialized <see cref="JsonElement"/>.</summary>
        /// <param name="method">Delegate to invoke.</param>
        /// <param name="args">Arguments to pass.</param>
        /// <returns>Wrapped result.</returns>
        object Invoke(Delegate method, object[] args)
        {
            return WrapResult(method.DynamicInvoke(args));
        }

        /// <summary>Serialize <paramref name="result"/> to a <see cref="JsonElement"/> using the pre/main/fallback chain.</summary>
        /// <param name="result">Object to wrap.</param>
        /// <returns>A <see cref="JsonElement"/> representation, or <c>null</c> if the result is null.</returns>
        object WrapResult(object result)
        {
            return result == null ? null : _chain.SerializeToElement(result, result.GetType());
        }

        /// <summary>Try to get the default value for a parameter from its attributes or compile-time default.</summary>
        /// <param name="param">Parameter to inspect.</param>
        /// <param name="value">Default value when the method returns <c>true</c>.</param>
        /// <returns><c>true</c> if a default value was found.</returns>
        static bool TryGetDefaultValue(ParameterInfo param, out object value)
        {
            if (param.HasDefaultValue)
            {
                value = param.DefaultValue;
                return true;
            }
            var attr = (DefaultValueAttribute)Attribute.GetCustomAttribute(param, typeof(DefaultValueAttribute));
            if (attr != null)
            {
                value = ResolveDefaultValue(attr.Value, param.ParameterType);
                return true;
            }
            // Some hosts (e.g. Unity) use their own DefaultValueAttribute-shaped type instead of
            // System.ComponentModel's. Probe any additionally registered attribute type via reflection.
            foreach (Type attrType in AdditionalDefaultValueAttributeTypes)
            {
                Attribute extra = Attribute.GetCustomAttribute(param, attrType);
                if (extra != null)
                {
                    PropertyInfo valueProp = attrType.GetProperty("Value");
                    if (valueProp != null)
                    {
                        value = ResolveDefaultValue(valueProp.GetValue(extra), param.ParameterType);
                        return true;
                    }
                }
            }
            value = null;
            return false;
        }

        /// <summary>Convert <paramref name="attrValue"/> to <paramref name="targetType"/>, handling enums and type conversions.</summary>
        /// <param name="attrValue">Raw attribute value.</param>
        /// <param name="targetType">Desired target type.</param>
        /// <returns>Converted value.</returns>
        static object ResolveDefaultValue(object attrValue, Type targetType)
        {
            if (attrValue == null)
            {
                return null;
            }

            if (targetType.IsAssignableFrom(attrValue.GetType()))
            {
                return attrValue;
            }

            if (targetType.IsEnum && attrValue is string s)
            {
                int dot = s.LastIndexOf('.');
                string memberName = dot >= 0 ? s[(dot + 1)..] : s;
                return Enum.Parse(targetType, memberName);
            }
            return Convert.ChangeType(attrValue, targetType);
        }
    }
}
