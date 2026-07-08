# UniLiquidLink — API Summary

## ITransportServer
*Namespace: LLiquidLink*

Transport layer abstraction for bidirectional binary communication with clients.

### Properties
- `ClientId` — ID assigned to the most recently connected client.

### Methods
- `Start` — Start listening for incoming connections.
- `Stop` — Stop the server and close all connections.
- `SendAll(ArraySegment{Byte})` — Send `data` to all currently connected clients.

### Events
- `OnConnect` — Fired when a new client connects. Parameters: client ID, remote endpoint string.
- `OnDisconnect` — Fired when a client disconnects. Parameter: client ID.
- `OnData` — Fired when binary data arrives from a client. Parameters: client ID, data segment.
- `OnError` — Fired when a transport-level error occurs. Parameters: client ID, exception.

---

## RpcRegistrar
*Namespace: LLiquidLink*

Registers RPC methods, JSON converters, and property accessors on an RpcBus.

### Constructors
- `__init__(RpcBus, JsonSerializerChain, Func{ILogger}, TypeResolver)` — Initialize the registrar and register the built-in `JsonRpc_ResolveChain` method.

### Methods
- `AddRpcConverter``2(RpcJsonConverter{``0,``1})` — Register a JSON converter that maps between an RPC wire type and its original .NET type. The converter is added to the shared JsonSerializerOptions and the RPC type map.
- `AddFallbackConverterFactory(JsonConverterFactory)` — Register a converter factory on the fallback JSON options, tried when the primary serializer fails.
- `AddRpcMethod``1(``0, RpcOptions)` — Register a delegate as an RPC method. The RPC name is derived from the delegate's declaring type and method name unless `options`.SimpleCall is `true`.
- `AddRpcGetProperty``2(Expression{Func{``0,``1}})` — Register a property getter so it can be accessed via chain resolution.
- `AddRpcRootGetProperty``1(string, Func{``0})` — Register a root-level property getter (for null-obj chain resolution). Accessible when the Python proxy starts a chain from self (obj is JSON null).
- `AddRpcSetProperty``2(Expression{Func{``0,``1}})` — Register a property setter so it can be assigned via chain resolution.
- `AddRpcDirectMethod``1(Expression{``0})` — Register a method for direct instance dispatch so it can be called on a deserialized object via chain resolution. The RPC name is prefixed with `"_"`.
- `AddRpcAllMethod(Type, RpcOptions)` — Register every public method of `type` (instance and static) as an RPC method. Generic and special-name (property/operator/event) methods are skipped. Overloads share one RPC name and are resolved at call time by trying each candidate in registration order.
- `AddRpcAllDirectMethod(Type, RpcOptions)` — Direct-dispatch variant of RpcOptions). Instance methods are additionally indexed for chain resolution and RPC names are prefixed with `"_"`.
- `AddRpcAllGetProperty(Type, RpcOptions)` — Register getters for every public field and property of `type` (instance and static) so they can be read via chain resolution.
- `AddRpcAllSetProperty(Type, RpcOptions)` — Register setters for every writable public field and property of `type` (instance and static) so they can be assigned via chain resolution.
- `MemberFlags(bool)` — Binding flags for public member enumeration; declared-only unless `includeInherited` is set.
- `EnumerateSelfAndNestedTypes(Type, bool)` — Yield `type` itself and, when `includeNested` is set, all its public nested types recursively.
- `EnumerateMethods(Type, bool)` — Enumerate registrable public methods of `type`, grouped by name so overloads stay together.
- `HasUnsupportedParameters(MethodBase)` — Return `true` if any parameter is by-ref, out, or a pointer (not deserializable from JSON).
- `MakeCandidate(Type, MethodInfo)` — Build an overload candidate that invokes `m` declared on `type`.
- `JsonRpc_ResolveChain(JsonElement, RpcChainStep[], string, JsonElement)` — Resolve a chain of property accesses and a terminal method call on a Unity object, dispatching each step server-side. Called via the registered `JsonRpc_ResolveChain` RPC method.
- `JsonRpc_ResolveChainSet(JsonElement, RpcChainStep[], string, JsonElement)` — Resolve a chain of property accesses on a Unity object and assign a value to the terminal property. Called via the registered `JsonRpc_ResolveChainSet` RPC method.
- `DeserializeRoot(JsonElement)` — Deserialize the root Unity object descriptor into a live instance using its `rpcType`. When the descriptor also carries an `orgType` (the concrete .NET type name), that type is resolved and used instead of the coarse type registered for `rpcType`, so the deserialized value matches the sender's actual concrete type rather than the converter's declared base type.
- `DeserializeWithFallback(string, Type)` — Deserialize raw JSON to a target type using the pre/main/fallback chain.
- `SaveRpcNamesCsv(string)` — Write all registered RPC method names to a CSV file (full_name, class_name, method_name).
- `ResolveStep(object, string, JsonElement[])` — Resolve a single step: try registered property getters first, then direct method dispatch.

---

## RpcJsonConverter`2
*Namespace: LLiquidLink*

Base class for JSON converters that translate between an original .NET type and an RPC-wire DTO type.

### Properties
- `rpcTypeName` — Fully qualified name of the RPC DTO type used as a discriminator on the wire.
- `orgType` — The original .NET type this converter handles.

### Methods
- `DtoOptions` — Options used to (de)serialize the wire DTO itself, independent of whichever JsonSerializerOptions instance (main/pre/fallback) hosts this converter. The DTO types only contain primitive fields, so no custom converters or resolvers are needed; reusing the runtime-supplied options here would break under a restricted resolver (e.g. the pre-chain's ConverterOnlyResolver) that requires every type it resolves to have its own registered converter.

---

## JsonPrimitiveHelper
*Namespace: LLiquidLink*

Shared helpers for reading/writing JSON primitives directly (no wrapper DTO).

### Methods
- `IsPrimitive(object)` — True for CLR primitive-ish types that should bypass the Unity-object registry envelope.
- `ReadRaw(Utf8JsonReader@)` — Read the current JSON token directly as a boxed CLR primitive.
- `WriteRaw(Utf8JsonWriter, object)` — Write a boxed CLR primitive directly as its native JSON token.

---

## IMainThreadDispatcher
*Namespace: LLiquidLink*

Abstraction for dispatching actions onto Unity's main thread.

### Methods
- `Enqueue(Action)` — Enqueue `action` to be executed on the main thread.
- `Start` — Start draining the action queue on the main thread.
- `Stop` — Stop draining the queue and unregister from the main-thread update callback.

---

## StdioTransport
*Namespace: LLiquidLink*

Reads JSON-RPC requests from a stream with 4-byte big-endian length framing, dispatches via RpcBus, and writes responses.

### Constructors
- `__init__(IMainThreadDispatcher, RpcBus, JsonSerializerOptions, Func{ILogger}, Action{Exception})` — Initialize StdioTransport with its dependencies.

### Methods
- `Start(Stream, Stream)` — Start the read loop on a background thread and fire OnConnect.
- `Stop` — Signal the read loop to stop.

### Events
- `OnConnect` — Fired on the main thread when the stdio connection is established.
- `OnDisconnect` — Fired on the main thread when the stdin stream closes.

---

## LogLevel
*Namespace: LLiquidLink.Logger*

Severity levels for the built-in logger.

---

## ILogger
*Namespace: LLiquidLink.Logger*

Logging interface for Server diagnostic output.

### Properties
- `MinLevel` — Minimum severity level; messages below this level are suppressed.

### Methods
- `Info(string)` — Log an informational message.
- `Debug(string)` — Log a debug-level message.
- `InfoFormat(string, Object[])` — Log a formatted informational message.
- `DebugFormat(string, Object[])` — Log a formatted debug-level message.

---

## TypeResolver
*Namespace: LLiquidLink*

Resolves .NET type names to Type objects within a curated set of assemblies.

### Constructors
- `__init__(Func{ILogger})` — Initialize the resolver with a logger factory.

### Methods
- `RegisterCallerAssembly` — Register the assembly of the direct caller and all assemblies it references. Must not be inlined so the calling assembly is detected correctly.
- `RegisterAssembly(Assembly)` — Register `asm` and all assemblies it references.
- `AddAssembly(Assembly)` — Add a single assembly to the allowed set and index all its exported types.
- `SaveAllowedTypesCsv(string)` — Write the full names of all indexed types to a CSV file. The Python gateway reads this file to resolve short type names sent by clients.
- `Resolve(string)` — Resolve a fully qualified type name to a Type within the registered assemblies. The Python gateway is responsible for expanding short names to full names before calling this method.

---

## RpcBus
*Namespace: LLiquidLink*

Routes incoming JSON-RPC calls to registered delegate or direct-method handlers.

### Constructors
- `__init__(Func{ILogger}, JsonSerializerChain)` — Initialize the bus with shared serialization infrastructure.

### Properties
- `RegisteredRpcNames` — Names of all registered RPC handlers.

### Methods
- `AdditionalDefaultValueAttributeTypes` — Additional attribute types (beyond DefaultValueAttribute) that expose a public `Value` property, checked via reflection when resolving omitted RPC parameter defaults. Unity integrations (e.g. `UnityEngine.Internal.DefaultValueAttribute`) register their attribute type here at startup so this class never references UnityEngine directly. Only ever written once from a static constructor before any dispatch runs, so concurrent writes are not a concern.
- `Register(string, Delegate)` — Register a delegate under `rpcName`. Arguments are deserialized from JSON using the delegate's parameter types.
- `RegisterDirect(string, Type, string, ParameterInfo[], Func{Object[],Object}, string)` — Register a method for direct instance dispatch. The first JSON argument is deserialized as the instance; remaining arguments are matched to `methodParams`.
- `RegisterMethodOverloads(string, IList{MethodCandidate})` — Register one or more overload candidates under `rpcName`. On dispatch, each candidate is tried in order and the first whose arguments deserialize successfully is invoked.
- `RegisterDirectMethodOverloads(string, string, IList{MethodCandidate})` — Register direct-dispatch overload candidates. Like MethodCandidate}), but instance candidates are also indexed by `methodName` so they can be reached via chain resolution.
- `BuildCandidateArgs(MethodCandidate, JsonElement[])` — Build the call-argument array for a single overload candidate, throwing if the JSON arguments don't fit.
- `DispatchCandidates(string, IList{MethodCandidate}, JsonElement[])` — Try each overload candidate in order; invoke the first whose arguments deserialize successfully.
- `DispatchDirectWithObj(object, string, JsonElement[])` — Invoke a directly-registered method on a pre-deserialized instance. Called from chain resolution on the main thread.
- `Dispatch(string, JsonElement[])` — Dispatch a registered RPC method by name with the given JSON arguments.
- `BuildInstanceCallArgs(object, JsonElement[], ParameterInfo[])` — Build the full argument array for an instance method call, filling defaults for omitted params.
- `DeserializeArg(JsonElement, Type)` — Deserialize a single JSON element to `orgType` using the pre/main/fallback chain.
- `DeserializeArgs(JsonElement[], ParameterInfo[])` — Deserialize a JSON argument array, filling defaults for omitted trailing parameters.
- `Invoke(Delegate, Object[])` — Invoke a delegate and wrap the result as a serialized JsonElement.
- `WrapResult(object)` — Serialize `result` to a JsonElement using the pre/main/fallback chain.
- `TryGetDefaultValue(ParameterInfo, Object@)` — Try to get the default value for a parameter from its attributes or compile-time default.
- `ResolveDefaultValue(object, Type)` — Convert `attrValue` to `targetType`, handling enums and type conversions.

---

## MethodCandidate
*Namespace: LLiquidLink.RpcBus*

Describes one overload candidate of an RPC method: how to build its arguments and invoke it.

### Methods
- `IsStatic` — When `true`, all JSON arguments map to MethodParams and no instance argument is consumed.
- `InstanceType` — For instance methods, the type used to deserialize the first JSON argument (the instance).
- `MethodParams` — Parameter descriptors of the target method (excluding the instance parameter).
- `Body` — Invocation body: receives `[arg0, ...]` for static methods or `[instance, arg0, ...]` for instance methods.
- `FullName` — Fully qualified method name for logging.

---

## IExecutorServer
*Namespace: LLiquidLink*

Public interface for an RPC executor server.

### Properties
- `Logger` — Logger instance used for diagnostic output.
- `OnError` — Callback invoked when a transport-level or RPC error occurs.

---

## Server
*Namespace: LLiquidLink*

Assembles and owns the Unity-independent half of the WebSocket-RPC stack: transport wiring, serializer chain, object/type registries, and RPC dispatch. Host-specific concerns (default converters, default logger, working-directory resolution) are exposed as virtual hooks for a subclass (e.g. `UniLiquidLink.Server`) to fill in.

### Constructors
- `__init__(IMainThreadDispatcher)` — Stdio-transport constructor; starts Python middleware and communicates via stdio.
- `__init__(ITransportServer, IMainThreadDispatcher)` — Injection constructor for unit tests: accepts a pre-wired transport and dispatcher.

### Properties
- `WorkingDirectory` — Path to the WebSocketLib directory, used to locate pysharpexecutor_server.
- `Rpc` — Registrar used to add RPC methods, converters, and property accessors.
- `Logger`
- `IsRunning` — True while the server is actively listening for connections.
- `TypeResolver` — The TypeResolver used to resolve .NET type names from RPC parameters.
- `OnError`

### Methods
- `RaiseOnDisconnect(int)` — Raise OnDisconnect. Field-like events can only be invoked from their declaring type, so subclasses use this instead.
- `CreateDefaultLogger` — Create the logger used when no other logger has been assigned. Override to supply a host-specific logger.
- `AddConverters` — Register additional main-stage converters. Override to add host-specific converters.
- `AddFallbackConverters` — Register additional fallback-stage converter factories. Override to add host-specific converters.
- `BuildCoreStack(JsonSerializerOptions@)` — Build the shared RPC core (serializer options, bus, registrar, registries, converters) used by both constructors. Transport wiring is left to each constructor.
- `Stop` — Stop the server and disconnect all clients.
- `FindConda(string)` — Resolve the conda executable by searching standard install locations under a user profile directory.
- `SendEvent(string, Dictionary{String,Object})` — Push a named event with optional payload to all connected Python clients.
- `RegisterCallerAssembly` — Register the assembly of the direct caller and all referenced assemblies for type resolution. Must not be inlined so the calling assembly is detected correctly.

### Events
- `OnDisconnect` — Fired when a client disconnects. Parameter: client ID.
- `OnServerError` — Fired when the Python server reports a startup or runtime error.

---

## NullLogger
*Namespace: LLiquidLink.Server*

No-op logger that silently discards all messages.

### Properties
- `MinLevel`

### Methods
- `Debug(string)`
- `Info(string)`
- `InfoFormat(string, Object[])`
- `DebugFormat(string, Object[])`

---

## ObjectRegistry
*Namespace: LLiquidLink*

Maps Unity object instance IDs to live Object references for RPC lookup.

### Constructors
- `__init__(Func{ILogger})` — Initialize the registry with a logger factory.

### Methods
- `_objectMap` — In-memory map from instance ID to registered Unity object.
- `GetObject(long)` — Look up a registered object by instance ID.
- `RegisterObject(object)` — Add `obj` to the in-memory map so it can be looked up by instance ID.
- `UnregisterObject(object)` — Remove `obj` from the in-memory map and fire OnRemoveObject.
- `ClearObjectMap` — Clear all entries from the in-memory map.
- `RemoveObject(int)` — Remove the entry for `instanceId` and fire OnRemoveObject if it existed.

### Events
- `OnRemoveObject` — Fired when an object is removed from the registry. Parameter: instance ID.

---

## JsonElementLeakException
*Namespace: LLiquidLink*

Thrown when deserialization yields a raw JsonElement instead of a converted value, meaning no stage actually produced a real CLR value for the target type.

---

## JsonSerializerChain
*Namespace: LLiquidLink*

Holds the pre/main/fallback JsonSerializerOptions stages and runs (de)serialization through them in order, returning the first stage's success.

### Constructors
- `__init__(JsonSerializerOptions, JsonSerializerOptions, JsonSerializerOptions)` — Build a chain from the three stage options.

### Properties
- `Pre` — Options for the pre stage (tried first; only succeeds for types with an explicit converter).
- `Main` — Options for the main stage (the general-purpose serializer).
- `Fallback` — Options for the fallback stage (tried when main fails, e.g. Unity value types via JsonUtility).

### Methods
- `Deserialize(string, Type)` — Deserialize `rawJson` to `type`, trying each stage in order.
- `SerializeToElement(object, Type)` — Serialize `value` to a JsonElement, trying each stage in order.

---

## RpcType
*Namespace: LLiquidLink*

Represents a .NET Type reference transmitted as a JSON-RPC parameter.

---

## RpcChainStep
*Namespace: LLiquidLink*

Single step in a property/method chain resolved server-side.

---

## RpcUnityObject
*Namespace: LLiquidLink*

JSON-serializable descriptor for a live Unity Object instance.

---

## RpcEnum
*Namespace: LLiquidLink*

Represents a .NET Type reference transmitted as a JSON-RPC parameter.

---

## JsonRpcProtocol
*Namespace: LLiquidLink*

Handles the 4-byte big-endian length prefix + JSON-RPC 2.0 protocol over WebSocket (test injection path).

### Constructors
- `__init__(RpcBus, Action{Byte[]}, Func{ILogger}, JsonSerializerOptions, Action{Exception})` — Initialize the protocol handler.

### Methods
- `HandleMessage(Byte[])` — Parse and dispatch a raw WebSocket message, then send the JSON-RPC response.
- `SendBytes(Byte[])` — Log and transmit a raw byte response to the client.

---

## Sample2

Integration test harness that boots an Server on port 8765 and registers sample RPC methods used by the Python integration tests.

### Constructors
- `__init__` — Create the server, configure loggers, and register all sample RPC methods.

### Methods
- `Obj` — Root transform used by integration tests that exercise chain resolution.
- `DebugProp` — Arbitrary integer property used to verify server state during debugging.
- `listLogger` — In-memory logger that accumulates log entries for golden-file comparison.
- `SampleMethodInt(int)` — Echo `x` back to the caller after logging.
- `SampleMethodIntStr(int, string)` — Echo `x` back after logging both arguments.
- `SampleGameObject(GameObject)` — Echo a GameObject reference back to the caller after logging.
- `SampleVector3(Vector3)` — Echo a Vector3 back to the caller after logging.
- `SampleGameObjectArray(GameObject[])` — Echo a GameObject array back to the caller after logging.
- `SampleGameObjectList(List{GameObject})` — Echo a List`1 of GameObjects back to the caller after logging.
- `SampleGameObjectDict(Dictionary{String,GameObject})` — Echo a Dictionary`2 of GameObjects back to the caller after logging.
- `DebugStart` — Start the test server via the Debug menu, running integration test setup first.
- `DebugStop` — Stop the test server, write accumulated log entries to Sample2.txt with instance IDs normalized to 0, and clear the singleton.
- `DebugDebug` — Ad-hoc debug menu entry for manual inspection during development.

---

## JsonUtilityConverterFactory
*Namespace: UniLiquidLink*

Routes matching value types to JsonUtility so System.Text.Json can compose them inside collections.

---

## JsonUtilityConverter`1
*Namespace: UniLiquidLink*

Delegates single-value (de)serialization to Unity's JsonUtility, bridging System.Text.Json tokens via a raw JSON string.

---

## Server
*Namespace: UniLiquidLink*

Unity-specific entry point for the WebSocket-RPC stack; adds Python-middleware process management, Unity object registration, and Unity-flavored logging on top of the Unity-independent Server base class.

### Constructors
- `__init__` — Production constructor; starts Python middleware and communicates via stdio.
- `__init__(ITransportServer, IMainThreadDispatcher)` — Injection constructor for unit tests: accepts a pre-wired transport and dispatcher.

### Methods
- `CreateDefaultLogger`
- `Start` — Start the server, register transport event handlers, and begin processing connections.
- `RegisterObject(Object)` — Register `obj` in the object registry so it can be referenced by instance ID.
- `UnregisterObject(Object)` — Remove `obj` from the object registry.

---

## DefaultLogger
*Namespace: UniLiquidLink.Server*

Default logger that forwards all messages to `UnityEngine.Debug.LogError`.

### Constructors
- `__init__` — Initialize with Info as the default minimum level.

### Properties
- `MinLevel`

### Methods
- `Info(string)`
- `Debug(string)`
- `InfoFormat(string, Object[])`
- `DebugFormat(string, Object[])`

---

## MainThreadDispatcher
*Namespace: UniLiquidLink*

Drains an action queue on Unity's main thread via update.

### Methods
- `Enqueue(Action)` — Enqueue `action` for execution on the main thread.
- `Start` — Register the drain loop with update.
- `Stop` — Unregister the drain loop from update.
- `ProcessAll` — Dequeue and invoke all pending actions in the current update tick.

---

## UEAssert

Assert wrapper that logs expected/actual values to the Test Console before delegating to NUnit Assert.

---

## GenerateRpcGolden

Generates golden JSON files for RpcIntegrationTests. Menu: UniLiquidLink > Generate RPC Golden Files Run this once after any intentional behavior change to update the golden files.

---

## SetupIntegrationTest

Sets up and tears down the Unity environment for integration tests. Menu: UniLiquidLink > Setup Integration Test UniLiquidLink > Teardown Integration Test

---

## ExportErrorLogs

Editor window that exports Unity console error entries to a text file.

### Methods
- `ExportLogs` — Export all error-level console entries to a user-chosen text file.
- `FindType(string)` — Search loaded assemblies for a type by full name. Tries UnityEditor assembly first, then all loaded assemblies, then a forced load of "UnityEditor".

---

## ConsoleWindowMode
*Namespace: ExportErrorLogs*

Subset of Unity console window mode flags used for error classification.

### Methods
- `Error` — Standard error entry.
- `Assert` — Assertion failure entry.
- `ScriptingError` — Scripting runtime error entry.
- `ScriptingException` — Unhandled scripting exception entry.

---
