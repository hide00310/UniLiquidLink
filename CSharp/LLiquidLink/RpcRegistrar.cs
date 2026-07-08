using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ILogger = LLiquidLink.Logger.ILogger;

namespace LLiquidLink
{
    /// <summary>Registers RPC methods, JSON converters, and property accessors on an <see cref="RpcBus"/>.</summary>
    public class RpcRegistrar
    {
        readonly Func<ILogger> _getLogger;
        readonly JsonSerializerChain _chain;
        readonly Dictionary<string, Type> _rpcTypeToOrgType = new Dictionary<string, Type>();
        readonly Dictionary<(Type ObjType, string PropertyName), (Type PropertyType, Delegate Method)> _rpcProperties
            = new Dictionary<(Type, string), (Type, Delegate)>();
        readonly Dictionary<(Type ObjType, string PropertyName), (Type PropertyType, Action<object, object> Setter)> _rpcSetProperties
            = new Dictionary<(Type, string), (Type, Action<object, object>)>();
        readonly RpcBus _bus;
        readonly TypeResolver _typeResolver;

        /// <summary>Initialize the registrar and register the built-in <c>JsonRpc_ResolveChain</c> method.</summary>
        /// <param name="bus">RPC bus to register methods on.</param>
        /// <param name="chain">Pre/main/fallback JSON serializer stage chain.</param>
        /// <param name="getLogger">Factory that returns the current logger.</param>
        /// <param name="typeResolver">Resolver used to turn a root object's <c>orgType</c> name into its concrete <see cref="Type"/>.</param>
        public RpcRegistrar(RpcBus bus, JsonSerializerChain chain, Func<ILogger> getLogger, TypeResolver typeResolver = null)
        {
            _getLogger = getLogger;
            _chain = chain;
            _typeResolver = typeResolver;
            _bus = bus;
            _bus.Register("JsonRpc_ResolveChain",
                (Func<JsonElement, RpcChainStep[], string, JsonElement, object>)JsonRpc_ResolveChain);
            _bus.Register("JsonRpc_ResolveChainSet",
                (Func<JsonElement, RpcChainStep[], string, JsonElement, object>)JsonRpc_ResolveChainSet);
        }

        /// <summary>
        /// Register a JSON converter that maps between an RPC wire type and its original .NET type.
        /// The converter is added to the shared <see cref="JsonSerializerOptions"/> and the RPC type map.
        /// </summary>
        /// <typeparam name="TOrg">Original .NET type.</typeparam>
        /// <typeparam name="TRpc">RPC wire DTO type.</typeparam>
        /// <param name="converter">Converter instance to register.</param>
        public void AddRpcConverter<TOrg, TRpc>(RpcJsonConverter<TOrg, TRpc> converter)
            where TOrg : class
            where TRpc : class
        {
            _chain.Main.Converters.Add(converter);
            _rpcTypeToOrgType[converter.rpcTypeName] = converter.orgType;
        }

        /// <summary>Register a converter factory on the fallback JSON options, tried when the primary serializer fails.</summary>
        /// <param name="factory">Converter factory to add.</param>
        public void AddFallbackConverterFactory(JsonConverterFactory factory)
        {
            _chain.Fallback.Converters.Add(factory);
        }

        public void AddPreConverter<TOrg, TRpc>(RpcJsonConverter<TOrg, TRpc> converter)
            where TOrg : class
            where TRpc : class
        {
            _chain.Pre.Converters.Add(converter);
        }

        /// <summary>
        /// Register a delegate as an RPC method. The RPC name is derived from the delegate's declaring type
        /// and method name unless <paramref name="options"/>.SimpleCall is <c>true</c>.
        /// </summary>
        /// <typeparam name="TDelegate">Delegate type.</typeparam>
        /// <param name="handler">Delegate to register.</param>
        /// <param name="options">Registration options. If <c>null</c>, uses defaults.</param>
        public void AddRpcMethod<TDelegate>(TDelegate handler, RpcOptions options = null) where TDelegate : Delegate
        {
            options ??= new RpcOptions();
            string rpcName = handler.Method.DeclaringType?.FullName + "." + handler.Method.Name;
            _bus.Register(rpcName, handler);
        }

        /// <summary>Register a property getter so it can be accessed via chain resolution.</summary>
        /// <typeparam name="TObj">Object type that owns the property.</typeparam>
        /// <typeparam name="TResult">Property value type.</typeparam>
        /// <param name="expr">Lambda expression selecting the property, e.g. <c>x => x.transform</c>.</param>
        public void AddRpcGetProperty<TObj, TResult>(Expression<Func<TObj, TResult>> expr)
        {
            var memberExpr = (MemberExpression)expr.Body;
            string propertyName = memberExpr.Member.Name;
            Type objType = typeof(TObj);
            Type propertyType = typeof(TResult);
            _rpcProperties.Add((objType, propertyName), (propertyType, expr.Compile()));
            _getLogger().DebugFormat("AddRpcGetProperty: {0}, {1}, {2}", objType, propertyType, propertyName);
        }

        /// <summary>
        /// Register a root-level property getter (for null-obj chain resolution).
        /// Accessible when the Python proxy starts a chain from self (obj is JSON null).
        /// </summary>
        /// <typeparam name="TResult">Property value type.</typeparam>
        /// <param name="name">Property name as seen by the chain resolver.</param>
        /// <param name="getter">Zero-argument getter returning the root property value.</param>
        public void AddRpcRootGetProperty<TResult>(string name, Func<TResult> getter)
        {
            Func<object, TResult> wrapper = _ => getter();
            _rpcProperties[(null, name)] = (typeof(TResult), wrapper);
            _getLogger().DebugFormat("AddRpcRootGetProperty: {0}", name);
        }

        /// <summary>Register a property setter so it can be assigned via chain resolution.</summary>
        /// <typeparam name="TObj">Object type that owns the property.</typeparam>
        /// <typeparam name="TResult">Property value type.</typeparam>
        /// <param name="expr">Lambda expression selecting the property, e.g. <c>x => x.position</c>.</param>
        public void AddRpcSetProperty<TObj, TResult>(Expression<Func<TObj, TResult>> expr)
        {
            var memberExpr = (MemberExpression)expr.Body;
            MemberInfo member = memberExpr.Member;
            string propertyName = member.Name;
            Type objType = typeof(TObj);
            Type propertyType = typeof(TResult);
            // Build a reflection-based setter (avoids Expression.Lambda compilation).
            Action<object, object> setter;
            PropertyInfo propInfo = member as PropertyInfo;
            if (propInfo != null)
            {
                setter = (instance, value) => propInfo.SetValue(instance, value);
            }
            else
            {
                FieldInfo fieldInfo = (FieldInfo)member;
                setter = (instance, value) => fieldInfo.SetValue(instance, value);
            }
            _rpcSetProperties.Add((objType, propertyName), (propertyType, setter));
            _getLogger().DebugFormat("AddRpcSetProperty: {0}, {1}, {2}", objType, propertyType, propertyName);
        }

        /// <summary>
        /// Register a method for direct instance dispatch so it can be called on a deserialized object
        /// via chain resolution. The RPC name is prefixed with <c>"_"</c>.
        /// </summary>
        /// <typeparam name="TDelegate">Delegate type whose body is a method call expression.</typeparam>
        /// <param name="handler">Expression containing the method call, e.g. <c>(Transform t) => t.Rotate(...)</c>.</param>
        public void AddRpcDirectMethod<TDelegate>(Expression<TDelegate> handler) where TDelegate : Delegate
        {
            var call = (MethodCallExpression)handler.Body;
            var method = call.Method;
            string rpcName = "_" + method.DeclaringType?.FullName + "." + method.Name;
            string logName = (method.DeclaringType != null ? method.DeclaringType.FullName : "") + "." + method.Name;
            _bus.RegisterDirect(
                rpcName,
                handler.Parameters[0].Type,
                method.Name,
                method.GetParameters(),
                args => method.IsStatic
                    ? method.Invoke(null, args)
                    : method.Invoke(args[0], args.Skip(1).ToArray()),
                logName
            );
        }

        /// <summary>
        /// Register every public method of <paramref name="type"/> (instance and static) as an RPC method.
        /// Generic and special-name (property/operator/event) methods are skipped. Overloads share one RPC name
        /// and are resolved at call time by trying each candidate in registration order.
        /// </summary>
        /// <param name="type">Type whose public methods are registered.</param>
        /// <param name="options">Registration options. If <c>null</c>, uses defaults.</param>
        public void AddRpcAllMethod(Type type, RpcOptions options = null)
        {
            options ??= new RpcOptions();
            foreach (Type t in EnumerateSelfAndNestedTypes(type, options.IncludeNested))
            {
                foreach (var group in EnumerateMethods(t, options.IncludeInherited))
                {
                    string name = t.FullName.Replace('+', '.') + "." + group.Key;
                    var candidates = group.Select(m => MakeCandidate(t, m)).ToList();
                    _bus.RegisterMethodOverloads(name, candidates);
                }
            }
        }

        /// <summary>
        /// Direct-dispatch variant of <see cref="AddRpcAllMethod"/>. Instance methods are additionally indexed for
        /// chain resolution and RPC names are prefixed with <c>"_"</c>.
        /// </summary>
        /// <param name="type">Type whose public methods are registered.</param>
        /// <param name="options">Registration options. If <c>null</c>, uses defaults.</param>
        public void AddRpcAllDirectMethod(Type type, RpcOptions options = null)
        {
            options ??= new RpcOptions();
            foreach (Type t in EnumerateSelfAndNestedTypes(type, options.IncludeNested))
            {
                foreach (var group in EnumerateMethods(t, options.IncludeInherited))
                {
                    string name = "_" + t.FullName.Replace('+', '.') + "." + group.Key;
                    var candidates = group.Select(m => MakeCandidate(t, m)).ToList();
                    _bus.RegisterDirectMethodOverloads(name, group.Key, candidates);
                }
            }
        }

        /// <summary>
        /// Register getters for every public field and property of <paramref name="type"/> (instance and static)
        /// so they can be read via chain resolution.
        /// </summary>
        /// <param name="type">Type whose public members are registered.</param>
        /// <param name="options">Registration options. If <c>null</c>, uses defaults.</param>
        public void AddRpcAllGetProperty(Type type, RpcOptions options = null)
        {
            options ??= new RpcOptions();
            foreach (Type t in EnumerateSelfAndNestedTypes(type, options.IncludeNested))
            {
                foreach (PropertyInfo p in t.GetProperties(MemberFlags(options.IncludeInherited)))
                {
                    if (!p.CanRead || p.GetIndexParameters().Length > 0
                        || (options.IncludeInherited && p.DeclaringType == typeof(object)))
                    {
                        continue;
                    }
                    PropertyInfo prop = p;
                    bool isStatic = prop.GetGetMethod(true).IsStatic;
                    Func<object, object> getter = instance => prop.GetValue(isStatic ? null : instance);
                    _rpcProperties[(t, p.Name)] = (p.PropertyType, getter);
                    _getLogger().DebugFormat("AddRpcAllGetProperty: {0}.{1}", t, p.Name);
                }
                foreach (FieldInfo f in t.GetFields(MemberFlags(options.IncludeInherited)))
                {
                    FieldInfo field = f;
                    Func<object, object> getter = instance => field.GetValue(field.IsStatic ? null : instance);
                    _rpcProperties[(t, f.Name)] = (f.FieldType, getter);
                    _getLogger().DebugFormat("AddRpcAllGetProperty: {0}.{1}", t, f.Name);
                }
            }
        }

        /// <summary>
        /// Register setters for every writable public field and property of <paramref name="type"/> (instance and static)
        /// so they can be assigned via chain resolution.
        /// </summary>
        /// <param name="type">Type whose public members are registered.</param>
        /// <param name="options">Registration options. If <c>null</c>, uses defaults.</param>
        public void AddRpcAllSetProperty(Type type, RpcOptions options = null)
        {
            options ??= new RpcOptions();
            foreach (Type t in EnumerateSelfAndNestedTypes(type, options.IncludeNested))
            {
                foreach (PropertyInfo p in t.GetProperties(MemberFlags(options.IncludeInherited)))
                {
                    if (!p.CanWrite || p.GetIndexParameters().Length > 0
                        || (options.IncludeInherited && p.DeclaringType == typeof(object)))
                    {
                        continue;
                    }
                    PropertyInfo prop = p;
                    bool isStatic = prop.GetSetMethod(true).IsStatic;
                    void setter(object instance, object value)
                    {
                        prop.SetValue(isStatic ? null : instance, value);
                    }

                    _rpcSetProperties[(t, p.Name)] = (p.PropertyType, setter);
                    _getLogger().DebugFormat("AddRpcAllSetProperty: {0}.{1}", t, p.Name);
                }
                foreach (FieldInfo f in t.GetFields(MemberFlags(options.IncludeInherited)))
                {
                    if (f.IsInitOnly || f.IsLiteral)
                    {
                        continue;
                    }
                    FieldInfo field = f;
                    void setter(object instance, object value)
                    {
                        field.SetValue(field.IsStatic ? null : instance, value);
                    }

                    _rpcSetProperties[(t, f.Name)] = (f.FieldType, setter);
                    _getLogger().DebugFormat("AddRpcAllSetProperty: {0}.{1}", t, f.Name);
                }
            }
        }

        /// <summary>Binding flags for public member enumeration; declared-only unless <paramref name="includeInherited"/> is set.</summary>
        /// <param name="includeInherited">When <c>true</c>, include inherited members.</param>
        /// <returns>The binding flags to use.</returns>
        static BindingFlags MemberFlags(bool includeInherited)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static;
            return includeInherited ? (flags | BindingFlags.FlattenHierarchy) : (flags | BindingFlags.DeclaredOnly);
        }

        /// <summary>Yield <paramref name="type"/> itself and, when <paramref name="includeNested"/> is set, all its public nested types recursively.</summary>
        /// <param name="type">Root type to enumerate.</param>
        /// <param name="includeNested">When <c>true</c>, recurse into public nested types.</param>
        /// <returns>The type followed by its public nested types (depth-first).</returns>
        static IEnumerable<Type> EnumerateSelfAndNestedTypes(Type type, bool includeNested)
        {
            yield return type;
            if (!includeNested)
            {
                yield break;
            }
            foreach (Type nested in type.GetNestedTypes(BindingFlags.Public))
            {
                foreach (Type t in EnumerateSelfAndNestedTypes(nested, true))
                {
                    yield return t;
                }
            }
        }

        /// <summary>Enumerate registrable public methods of <paramref name="type"/>, grouped by name so overloads stay together.</summary>
        /// <param name="type">Type to enumerate.</param>
        /// <param name="includeInherited">When <c>true</c>, include inherited methods (except those declared on <see cref="object"/>).</param>
        /// <returns>Method groups keyed by method name.</returns>
        static IEnumerable<IGrouping<string, MethodInfo>> EnumerateMethods(Type type, bool includeInherited)
        {
            return type.GetMethods(MemberFlags(includeInherited))
                .Where(m => !m.IsGenericMethodDefinition && !m.ContainsGenericParameters)
                .Where(m => !m.IsSpecialName)
                .Where(m => !HasUnsupportedParameters(m))
                .Where(m => !includeInherited || m.DeclaringType != typeof(object))
                .GroupBy(m => m.Name);
        }

        /// <summary>Return <c>true</c> if any parameter is by-ref, out, or a pointer (not deserializable from JSON).</summary>
        /// <param name="m">Method to inspect.</param>
        /// <returns><c>true</c> when the method has an unsupported parameter.</returns>
        static bool HasUnsupportedParameters(MethodBase m)
        {
            foreach (ParameterInfo p in m.GetParameters())
            {
                if (p.ParameterType.IsByRef || p.ParameterType.IsPointer)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>Build an overload candidate that invokes <paramref name="m"/> declared on <paramref name="type"/>.</summary>
        /// <param name="type">Owning type used as the instance type for non-static methods.</param>
        /// <param name="m">Method to wrap.</param>
        /// <returns>A candidate consumable by <see cref="RpcBus.RegisterMethodOverloads"/>.</returns>
        static RpcBus.MethodCandidate MakeCandidate(Type type, MethodInfo m)
        {
            string fullName = type.FullName + "." + m.Name;
            ParameterInfo[] methodParams = m.GetParameters();
            return m.IsStatic
                ? new RpcBus.MethodCandidate
                {
                    IsStatic = true,
                    InstanceType = null,
                    MethodParams = methodParams,
                    Body = a => m.Invoke(null, a),
                    FullName = fullName,
                }
                : new RpcBus.MethodCandidate
                {
                    IsStatic = false,
                    InstanceType = type,
                    MethodParams = methodParams,
                    Body = a => m.Invoke(a[0], a.Skip(1).ToArray()),
                    FullName = fullName,
                };
        }

        /// <summary>
        /// Resolve a chain of property accesses and a terminal method call on a Unity object,
        /// dispatching each step server-side. Called via the registered <c>JsonRpc_ResolveChain</c> RPC method.
        /// </summary>
        /// <param name="obj">JSON element describing the root Unity object (must contain <c>rpcType</c>).</param>
        /// <param name="steps">Intermediate property access steps.</param>
        /// <param name="method">Terminal method or property name to invoke.</param>
        /// <param name="args">JSON array of arguments for the terminal method.</param>
        /// <returns>The result of the terminal method call.</returns>
        public object JsonRpc_ResolveChain(JsonElement obj, RpcChainStep[] steps, string method, JsonElement args)
        {
            _getLogger().DebugFormat("JsonRpc_ResolveChain({0}, {1}, {2}, {3})", obj, steps, method, args);
            object current = obj.ValueKind == JsonValueKind.Null ? null : DeserializeRoot(obj);

            foreach (var step in steps)
            {
                current = ResolveStep(current, step.name, Array.Empty<JsonElement>());
            }

            JsonElement[] restArgs = args.ValueKind == JsonValueKind.Array
                ? args.EnumerateArray().ToArray()
                : Array.Empty<JsonElement>();
            return ResolveStep(current, method, restArgs);
        }

        /// <summary>
        /// Resolve a chain of property accesses on a Unity object and assign a value to the terminal property.
        /// Called via the registered <c>JsonRpc_ResolveChainSet</c> RPC method.
        /// </summary>
        /// <param name="obj">JSON element describing the root Unity object (must contain <c>rpcType</c>).</param>
        /// <param name="steps">Intermediate property access steps leading to the owner object.</param>
        /// <param name="property">Name of the property to set on the resolved owner object.</param>
        /// <param name="value">JSON value to deserialize and assign to the property.</param>
        /// <returns>Always <c>null</c>; assignment has no return value.</returns>
        public object JsonRpc_ResolveChainSet(JsonElement obj, RpcChainStep[] steps, string property, JsonElement value)
        {
            object current = obj.ValueKind == JsonValueKind.Null ? null : DeserializeRoot(obj);

            foreach (var step in steps)
            {
                current = ResolveStep(current, step.name, Array.Empty<JsonElement>());
            }

            for (Type t = current.GetType(); t != null; t = t.BaseType)
            {
                if (_rpcSetProperties.TryGetValue((t, property), out var setProperty))
                {
                    _getLogger().DebugFormat("SetProperty {0}.{1}", current, property);
                    object deserialized = DeserializeWithFallback(value.GetRawText(), setProperty.PropertyType);
                    setProperty.Setter(current, deserialized);
                    return null;
                }
            }
            throw new ArgumentException($"No set property '{property}' registered on {current.GetType()}");
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
            if (!obj.TryGetProperty("rpcType", out var rpcTypeProp)
                || !_rpcTypeToOrgType.TryGetValue(rpcTypeProp.GetString(), out Type targetType))
            {
                throw new ArgumentException($"RpcType not in {obj}");
            }

            if (obj.TryGetProperty("orgType", out var orgTypeProp) && orgTypeProp.ValueKind == JsonValueKind.String)
            {
                targetType = _typeResolver.Resolve(orgTypeProp.GetString());
            }

            return DeserializeWithFallback(obj.GetRawText(), targetType);
        }

        /// <summary>Deserialize raw JSON to a target type using the pre/main/fallback chain.</summary>
        /// <param name="rawJson">Raw JSON text to deserialize.</param>
        /// <param name="targetType">Target .NET type.</param>
        /// <returns>The deserialized value.</returns>
        object DeserializeWithFallback(string rawJson, Type targetType)
        {
            return _chain.Deserialize(rawJson, targetType);
        }

        /// <summary>Write all registered RPC method names to a CSV file (full_name, class_name, method_name).</summary>
        /// <param name="path">Absolute path of the CSV file to write.</param>
        public void SaveRpcNamesCsv(string path)
        {
            var sb = new StringBuilder();
            sb.AppendLine("full_name,class_name,method_name");
            foreach (string fullName in _bus.RegisteredRpcNames)
            {
                if (fullName.StartsWith("JsonRpc_") || fullName.StartsWith("OnServerError"))
                {
                    continue;
                }

                int lastDot = fullName.LastIndexOf('.');
                string methodName = lastDot >= 0 ? fullName[(lastDot + 1)..] : fullName;
                string prefix = lastDot >= 0 ? fullName[..lastDot] : "";
                int prevDot = prefix.LastIndexOf('.');
                string className = prevDot >= 0 ? prefix[(prevDot + 1)..] : prefix;
                sb.AppendLine(fullName + "," + className + "," + methodName);
            }
            File.WriteAllText(path, sb.ToString());
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
                if (_rpcProperties.TryGetValue((null, name), out var rootProperty))
                {
                    _getLogger().DebugFormat("GetRootProperty {0}", name);
                    return rootProperty.Method.DynamicInvoke(new object[] { null });
                }
                throw new ArgumentException("No root property '" + name + "' registered (obj is null)");
            }

            for (Type t = current.GetType(); t != null; t = t.BaseType)
            {
                if (_rpcProperties.TryGetValue((t, name), out var property))
                {
                    _getLogger().DebugFormat("GetProperty {0}.{1}", current, name);
                    return property.Method.DynamicInvoke(current);
                }
            }
            return _bus.DispatchDirectWithObj(current, name, stepArgs);
        }
    }
}
