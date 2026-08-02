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
    /// <summary>Registers RPC methods, JSON converters, and property accessors, owning the registration table read by <see cref="RpcSearcher"/>.</summary>
    public class RpcRegistry
    {
        readonly Func<ILogger> _getLogger;
        readonly RpcRegistrationTable _table = new RpcRegistrationTable();

        /// <summary>Read-only search interface over this registrar's registration table, used by <see cref="MethodCaller"/> for dispatch.</summary>
        public RpcSearcher Searcher { get; }

        /// <summary>
        /// Additional attribute types (beyond <see cref="System.ComponentModel.DefaultValueAttribute"/>) that expose
        /// a public <c>Value</c> property, checked via reflection when resolving omitted RPC parameter defaults.
        /// Unity integrations (e.g. <c>UnityEngine.Internal.DefaultValueAttribute</c>) register their
        /// attribute type here at startup so <see cref="MethodCaller"/> never references UnityEngine directly.
        /// Only ever written once from a static constructor before any dispatch runs, so concurrent
        /// writes are not a concern.
        /// </summary>
        public static readonly HashSet<Type> AdditionalDefaultValueAttributeTypes = new HashSet<Type>();

        /// <summary>Names of all registered RPC handlers.</summary>
        public IEnumerable<string> RegisteredRpcNames => _table.Router.Keys;

        /// <summary>Initialize the registrar.</summary>
        /// <param name="getLogger">Factory that returns the current logger.</param>
        /// <param name="typeResolver">Resolver used to turn a root object's <c>orgType</c> name into its concrete <see cref="Type"/>.</param>
        public RpcRegistry(Func<ILogger> getLogger, TypeResolver typeResolver = null)
        {
            _getLogger = getLogger;
            Searcher = new RpcSearcher(_table, typeResolver);
        }

        /// <summary>
        /// Register a delegate under <paramref name="rpcName"/>. Arguments are deserialized from JSON
        /// using the delegate's parameter types.
        /// </summary>
        /// <param name="rpcName">RPC method name clients use to call this handler.</param>
        /// <param name="method">Delegate to invoke.</param>
        public void Register(string rpcName, Delegate method)
        {
            string logName = (method.Method.DeclaringType != null ? method.Method.DeclaringType.FullName : "") + "." + method.Method.Name;
            var candidate = new RpcSearcher.MethodCandidate
            {
                IsStatic = true,
                InstanceType = null,
                MethodParams = method.Method.GetParameters(),
                Method = args => method.DynamicInvoke(args),
                FullName = logName,
            };
            RegisterMethodOverloads(rpcName, new List<RpcSearcher.MethodCandidate> { candidate });
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
            var candidate = new RpcSearcher.MethodCandidate
            {
                IsStatic = false,
                InstanceType = instanceType,
                MethodParams = methodParams,
                Method = body,
                FullName = logName,
            };
            RegisterDirectMethodOverloads(rpcName, methodName, new List<RpcSearcher.MethodCandidate> { candidate });
        }

        /// <summary>
        /// Register one or more overload candidates under <paramref name="rpcName"/>. On dispatch (via
        /// <see cref="MethodCaller.Call"/>), each candidate is tried in order and the first whose arguments
        /// deserialize successfully is invoked.
        /// </summary>
        /// <param name="rpcName">RPC method name clients use to call this handler.</param>
        /// <param name="candidates">Overload candidates, tried in order.</param>
        public void RegisterMethodOverloads(string rpcName, IList<RpcSearcher.MethodCandidate> candidates)
        {
            _getLogger().DebugFormat("Register: {0} ({1} overloads)", rpcName, candidates.Count);
            _table.Router[rpcName] = candidates;
        }

        /// <summary>
        /// Register direct-dispatch overload candidates. Like <see cref="RegisterMethodOverloads"/>, but instance
        /// candidates are also indexed by <paramref name="methodName"/> so they can be reached via chain resolution.
        /// </summary>
        /// <param name="rpcName">RPC method name clients use to call this handler (typically prefixed with <c>"_"</c>).</param>
        /// <param name="methodName">Method name used as the chain-resolution key.</param>
        /// <param name="candidates">Overload candidates, tried in order.</param>
        public void RegisterDirectMethodOverloads(string rpcName, string methodName, IList<RpcSearcher.MethodCandidate> candidates)
        {
            _getLogger().DebugFormat("RegisterDirect: {0} -> {1} ({2} overloads)", rpcName, methodName, candidates.Count);

            foreach (RpcSearcher.MethodCandidate c in candidates)
            {
                if (c.IsStatic)
                {
                    continue;
                }
                var key = (c.InstanceType, methodName);
                if (!_table.DirectEntries.TryGetValue(key, out List<RpcSearcher.MethodCandidate> list))
                {
                    list = new List<RpcSearcher.MethodCandidate>();
                    _table.DirectEntries[key] = list;
                }
                list.Add(c);
            }

            _table.Router[rpcName] = candidates;
        }

        /// <summary>
        /// Register a JSON converter that maps between an RPC wire type and its original .NET type.
        /// The converter is added to <paramref name="options"/> and the RPC type map.
        /// </summary>
        /// <typeparam name="TOrg">Original .NET type.</typeparam>
        /// <typeparam name="TRpc">RPC wire DTO type.</typeparam>
        /// <param name="options">Main-stage JSON serializer options to add the converter to.</param>
        /// <param name="converter">Converter instance to register.</param>
        public void AddConverterAndRegister<TOrg, TRpc>(JsonSerializerOptions options, RpcJsonConverter<TOrg, TRpc> converter)
            where TOrg : class
            where TRpc : class
        {
            options.Converters.Add(converter);
            _table.RpcTypeToOrgType[converter.rpcTypeName] = converter.orgType;
        }

        /// <summary>Register a converter factory on the fallback JSON options, tried when the primary serializer fails.</summary>
        /// <param name="options">Fallback-stage JSON serializer options to add the factory to.</param>
        /// <param name="converter">Converter factory to add.</param>
        public void AddConverter(JsonSerializerOptions options, JsonConverter converter)
        {
            options.Converters.Add(converter);
        }

        /// <summary>
        /// Register a delegate as an RPC method. The RPC name is always derived from the delegate's
        /// declaring type and method name.
        /// </summary>
        /// <typeparam name="TDelegate">Delegate type.</typeparam>
        /// <param name="handler">Delegate to register.</param>
        /// <param name="options">Accepted for API symmetry with the other AddRpc* overloads; unused here since a single delegate has no inherited/nested members to filter.</param>
        public void AddRpcMethod<TDelegate>(TDelegate handler, RpcOptions options = null) where TDelegate : Delegate
        {
            string rpcName = handler.Method.DeclaringType?.FullName + "." + handler.Method.Name;
            Register(rpcName, handler);
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
            _table.RpcProperties[(objType, propertyName)] = (propertyType, expr.Compile());
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
            _table.RpcProperties[(null, name)] = (typeof(TResult), wrapper);
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
            _table.RpcSetProperties[(objType, propertyName)] = (propertyType, setter);
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
            RegisterDirect(
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
                    RegisterMethodOverloads(name, candidates);
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
                    RegisterDirectMethodOverloads(name, group.Key, candidates);
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
                    _table.RpcProperties[(t, p.Name)] = (p.PropertyType, getter);
                    _getLogger().DebugFormat("AddRpcAllGetProperty: {0}.{1}", t, p.Name);
                }
                foreach (FieldInfo f in t.GetFields(MemberFlags(options.IncludeInherited)))
                {
                    FieldInfo field = f;
                    Func<object, object> getter = instance => field.GetValue(field.IsStatic ? null : instance);
                    _table.RpcProperties[(t, f.Name)] = (f.FieldType, getter);
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

                    _table.RpcSetProperties[(t, p.Name)] = (p.PropertyType, setter);
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

                    _table.RpcSetProperties[(t, f.Name)] = (f.FieldType, setter);
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
        /// <returns>A candidate consumable by <see cref="RegisterMethodOverloads"/>.</returns>
        static RpcSearcher.MethodCandidate MakeCandidate(Type type, MethodInfo m)
        {
            string fullName = type.FullName + "." + m.Name;
            ParameterInfo[] methodParams = m.GetParameters();
            return m.IsStatic
                ? new RpcSearcher.MethodCandidate
                {
                    IsStatic = true,
                    InstanceType = null,
                    MethodParams = methodParams,
                    Method = a => m.Invoke(null, a),
                    FullName = fullName,
                }
                : new RpcSearcher.MethodCandidate
                {
                    IsStatic = false,
                    InstanceType = type,
                    MethodParams = methodParams,
                    Method = a => m.Invoke(a[0], a.Skip(1).ToArray()),
                    FullName = fullName,
                };
        }

        /// <summary>Write all registered RPC method names to a CSV file (full_name, class_name, method_name).</summary>
        /// <param name="path">Absolute path of the CSV file to write.</param>
        public void SaveRpcNamesCsv(string path)
        {
            var sb = new StringBuilder();
            _ = sb.AppendLine("full_name,class_name,method_name");
            foreach (string fullName in RegisteredRpcNames)
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
                _ = sb.AppendLine(fullName + "," + className + "," + methodName);
            }
            File.WriteAllText(path, sb.ToString());
        }
    }
}
