using LLiquidLink.Logger;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace LLiquidLink
{
    /// <summary>
    /// Routes incoming JSON-RPC calls to handlers looked up via a <see cref="RpcSearcher"/>, deserializing
    /// arguments and serializing results, and resolves Unity object chain calls
    /// (<c>JsonRpc_ResolveChain</c>/<c>JsonRpc_ResolveChainSet</c>) against property/type state exposed by
    /// the searcher.
    /// </summary>
    public class MethodCaller
    {
        readonly Func<ILogger> _getLogger;
        readonly JsonSerializerChain _chain;
        readonly RpcSearcher _searcher;

        /// <summary>Initialize the caller. The built-in <c>JsonRpc_ResolveChain</c>/<c>JsonRpc_ResolveChainSet</c>
        /// methods must be registered separately (the caller only looks them up via <paramref name="searcher"/>).</summary>
        /// <param name="getLogger">Factory that returns the current logger.</param>
        /// <param name="chain">Serializer chain used for argument/result (de)serialization.</param>
        /// <param name="searcher">Read-only lookup over the registration table and property/type state used for chain resolution.</param>
        public MethodCaller(Func<ILogger> getLogger, JsonSerializerChain chain, RpcSearcher searcher)
        {
            _getLogger = getLogger;
            _chain = chain;
            _searcher = searcher;
        }

        /// <summary>Build the call-argument array for a single overload candidate, throwing if the JSON arguments don't fit.</summary>
        /// <param name="c">Overload candidate.</param>
        /// <param name="args">JSON argument elements.</param>
        /// <returns>Argument array ready for <see cref="RpcSearcher.MethodCandidate.Method"/>.</returns>
        object[] BuildCandidateArgs(RpcSearcher.MethodCandidate c, JsonElement[] args)
        {
            if (c.IsStatic)
            {
                return DeserializeArgs(args, c.MethodParams);
            }
            if (args.Length == 0)
            {
                throw new ArgumentException("Missing instance argument");
            }
            object instance = _chain.Deserialize(args[0].GetRawText(), c.InstanceType);
            var restArgs = new JsonElement[args.Length - 1];
            Array.Copy(args, 1, restArgs, 0, restArgs.Length);
            return BuildInstanceCallArgs(instance, restArgs, c.MethodParams);
        }

        /// <summary>Try each overload candidate in order; invoke the first whose arguments deserialize successfully.</summary>
        /// <param name="rpcName">RPC method name (for error messages).</param>
        /// <param name="candidates">Overload candidates to try.</param>
        /// <param name="args">JSON argument elements.</param>
        /// <returns>Wrapped result of the first matching candidate.</returns>
        object CallCandidates(string rpcName, IList<RpcSearcher.MethodCandidate> candidates, JsonElement[] args)
        {
            Exception? lastError = null;
            foreach (RpcSearcher.MethodCandidate c in candidates)
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
                _getLogger().DebugFormat("CallMethod {0}({1})", c.FullName, new ArrayLogFormatter(callArgs));
                object result = c.Method(callArgs);
                return result == null ? null : (object)_chain.SerializeToElement(result, result.GetType());
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
        public object CallDirectWithObj(object orgObj, string methodName, JsonElement[] restArgs)
        {
            List<RpcSearcher.MethodCandidate>? candidates = null;
            for (Type t = orgObj.GetType(); t != null; t = t.BaseType)
            {
                if (_searcher.TryGetDirectCandidates(t, methodName, out candidates))
                {
                    break;
                }
            }
            if (candidates == null)
            {
                throw new KeyNotFoundException(string.Format(
                    "Direct method '{0}' not found for type '{1}'", methodName, orgObj.GetType()));
            }

            Exception? lastError = null;
            foreach (RpcSearcher.MethodCandidate c in candidates)
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
                _getLogger().DebugFormat("CallMethod {0}({1})", c.FullName, new ArrayLogFormatter(callArgs));
                return c.Method(callArgs);
            }
            throw lastError ?? new KeyNotFoundException(string.Format(
                "No matching direct overload '{0}' for type '{1}'", methodName, orgObj.GetType()));
        }

        /// <summary>Dispatch a registered RPC method by name with the given JSON arguments.</summary>
        /// <param name="method">RPC method name.</param>
        /// <param name="args">JSON argument elements.</param>
        /// <returns>Result wrapped as a <see cref="JsonElement"/>.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the method name is not registered.</exception>
        public object Call(string method, JsonElement[] args)
        {
            return !_searcher.TryGetCandidates(method, out IList<RpcSearcher.MethodCandidate> candidates)
                ? throw new KeyNotFoundException("Method not found: " + method)
                : CallCandidates(method, candidates, args);
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
                    result[i] = _chain.Deserialize(args[i].GetRawText(), paramInfos[i].ParameterType);
                }
            }
            return result;
        }

        /// <summary>Try to get the default value for a parameter from its attributes or compile-time default.</summary>
        /// <param name="param">Parameter to inspect.</param>
        /// <param name="value">Default value when the method returns <c>true</c>.</param>
        /// <returns><c>true</c> if a default value was found.</returns>
        static bool TryGetDefaultValue(ParameterInfo param, out object? value)
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
            foreach (Type attrType in RpcRegistry.AdditionalDefaultValueAttributeTypes)
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
        static object? ResolveDefaultValue(object attrValue, Type targetType)
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

        // ── Chain resolution (property/type state stays on the injected registrar) ──

        /// <summary>
        /// Resolve a chain of property accesses and a terminal method call on a Unity object,
        /// dispatching each step server-side. Registered as the <c>JsonRpc_ResolveChain</c> RPC method.
        /// </summary>
        /// <param name="p">Parameter object bundling the root object, intermediate steps, terminal method name and its arguments.</param>
        /// <returns>The result of the terminal method call.</returns>
        public object JsonRpc_ResolveChain(RpcResolveChainParam p)
        {
            _getLogger().DebugFormat("JsonRpc_ResolveChain({0}, {1}, {2}, {3})", p.obj, new ArrayLogFormatter(p.steps), p.method, new ArrayLogFormatter(p.args));
            object? current = p.obj.ValueKind == JsonValueKind.Null ? null : DeserializeRoot(p.obj);

            foreach (var step in p.steps)
            {
                current = ResolveStep(current, step.name, Array.Empty<JsonElement>());
            }

            return ResolveStep(current, p.method, p.args ?? Array.Empty<JsonElement>());
        }

        /// <summary>
        /// Resolve a chain of property accesses on a Unity object and assign a value to the terminal property.
        /// Registered as the <c>JsonRpc_ResolveChainSet</c> RPC method.
        /// </summary>
        /// <param name="p">Parameter object bundling the root object, intermediate steps, terminal property name and its value.</param>
        /// <returns>Always <c>null</c>; assignment has no return value.</returns>
        public object? JsonRpc_ResolveChainSet(RpcResolveChainSetParam p)
        {
            object? current = p.obj.ValueKind == JsonValueKind.Null ? null : DeserializeRoot(p.obj);

            foreach (var step in p.steps)
            {
                current = ResolveStep(current, step.name, Array.Empty<JsonElement>());
            }

            if (current == null)
            {
                // Unlike JsonRpc_ResolveChain's terminal step, property assignment has no
                // root-level (null-obj) setter table to fall back to.
                throw new ArgumentException(string.Format("No set property '{0}' registered on root (obj is null)", p.property));
            }

            for (Type t = current.GetType(); t != null; t = t.BaseType)
            {
                if (_searcher.TryGetSetProperty(t, p.property, out var setProperty))
                {
                    _getLogger().DebugFormat("SetProperty {0}.{1}", current, p.property);
                    object deserialized = _chain.Deserialize(p.value.GetRawText(), setProperty.PropertyType);
                    setProperty.Setter(current, deserialized);
                    return null;
                }
            }
            throw new ArgumentException(string.Format("No set property '{0}' registered on {1}", p.property, current.GetType()));
        }

        /// <summary>Resolve a single step: try registered property getters first, then direct method dispatch.</summary>
        /// <param name="current">Current object in the resolution chain.</param>
        /// <param name="name">Property or method name to resolve.</param>
        /// <param name="stepArgs">Arguments for a method call step.</param>
        /// <returns>The result of the resolved property or method.</returns>
        object ResolveStep(object current, string name, JsonElement[] stepArgs)
        {
            if (current == null)
            {
                if (_searcher.TryGetRootProperty(name, out var rootProperty))
                {
                    _getLogger().DebugFormat("GetRootProperty {0}", name);
                    return rootProperty.Method.DynamicInvoke(new object[] { null });
                }
                throw new ArgumentException("No root property '" + name + "' registered (obj is null)");
            }

            for (Type t = current.GetType(); t != null; t = t.BaseType)
            {
                if (_searcher.TryGetProperty(t, name, out var property))
                {
                    _getLogger().DebugFormat("GetProperty {0}.{1}", current, name);
                    return property.Method.DynamicInvoke(current);
                }
            }
            return CallDirectWithObj(current, name, stepArgs);
        }

        /// <summary>
        /// Deserialize the root Unity object descriptor into a live instance using its <c>rpcType</c>.
        /// When the descriptor also carries an <c>orgType</c> (the concrete .NET type name), that type is
        /// resolved and used instead of the coarse type registered for <c>rpcType</c>, so the deserialized
        /// value matches the sender's actual concrete type rather than the converter's declared base type.
        /// </summary>
        /// <param name="obj">JSON element describing the root Unity object.</param>
        /// <returns>The deserialized root object.</returns>
        object DeserializeRoot(JsonElement obj)
        {
            var rpcObj = JsonSerializer.Deserialize<RpcInstanceObject>(obj);

            if (!_searcher.TryResolveRpcType(rpcObj.rpcType, out Type targetType))
            {
                throw new ArgumentException(string.Format("RpcType not in {0}", obj));
            }

            targetType = _searcher.ResolveOrgType(rpcObj.orgType);

            return _chain.Deserialize(obj.GetRawText(), targetType);
        }
    }
}
