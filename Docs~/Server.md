# UniLiquidLink — API Summary

## JsonElementLeakException
*Namespace: LLiquidLink*

Thrown when deserialization yields a raw JsonElement instead of a converted value, meaning no stage actually produced a real CLR value for the target type.

---

## JsonSerializerChain
*Namespace: LLiquidLink*

Holds the pre/main/fallback JsonSerializerOptions stages and runs (de)serialization through them in order, returning the first stage's success.

### Constructors
- `__init__` — Build a chain with the pre/main/fallback stage options fully initialized.

### Properties
- `Options` — The pre/main/fallback stage options, indexed by Stage. Fully initialized; callers add stage-specific converters after construction (e.g. `Options[(int)Stage.Main].Converters.Add(...)`).

### Methods
- `Deserialize(string, Type)` — Deserialize `rawJson` to `type`, trying each stage in order.
- `SerializeToElement(object, Type)` — Serialize `value` to a JsonElement, trying each stage in order.

---

## Stage
*Namespace: LLiquidLink.JsonSerializerChain*

Identifies a stage in the pre/main/fallback (de)serialization chain.

---

## ConverterOnlyResolver
*Namespace: LLiquidLink.JsonSerializerChain*

Type resolver for the pre stage: resolves only types with an explicitly registered converter, so any other type fails immediately and falls through to the main stage.

### Methods
- `HasConverter(Type, JsonSerializerOptions)` — True when the type itself has a registered converter, or is an array whose element type has one (so the array's element loop can reach that converter).

---

## IRpcNullChecker`1
*Namespace: LLiquidLink*

Strategy injected into a converter (constructor injection) that defines what counts as "null" for the converter's original type. Needed for types whose null-ness is not reference equality (e.g. Unity's destroyed / fake-null objects). Contravariant, so a checker for a base type can be injected into a converter of a derived type.

### Methods
- `IsNull(`0)` — Return `true` if `value` must be written as JSON null.

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
- `ReadRaw(Utf8JsonReader@)` — Read the current JSON token directly as a boxed CLR primitive.
- `WriteRaw(Utf8JsonWriter, object)` — Write a boxed CLR primitive directly as its native JSON token.

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

## ArrayLogFormatter
*Namespace: LLiquidLink.Logger*

Wraps an IEnumerable so its elements are rendered in log output instead of the array's type name.

### Constructors
- `__init__(IEnumerable)` — Initialize the formatter with the collection to render.

### Methods
- `ToString` — Render the collection as `[item1, item2, ...]`, using `"null"` for null elements. Returns `"null"` if the collection itself is null.

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
- `RemoveObject(long)` — Remove the entry for `instanceId` and fire OnRemoveObject if it existed.

### Events
- `OnRemoveObject` — Fired when an object is removed from the registry. Parameter: instance ID.

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

## Utils
*Namespace: LLiquidLink*

Small filesystem/reflection helpers shared across the core library.

### Methods
- `GetCurrentDirectory(string)` — Return the directory containing the caller's source file (via CallerFilePathAttribute).
- `ResolveDataDir(string)` — Resolve the absolute path of the Data~ directory next to serverDir.
- `UnwrapTargetInvocation(Exception)` — Unwrap reflection's TargetInvocationException to expose the actual thrown exception.

---

## MethodCaller
*Namespace: LLiquidLink*

Routes incoming JSON-RPC calls to handlers looked up via a RpcSearcher, deserializing arguments and serializing results, and resolves Unity object chain calls (`JsonRpc_ResolveChain`/`JsonRpc_ResolveChainSet`) against property/type state exposed by the searcher.

### Constructors
- `__init__(Func{ILogger}, JsonSerializerChain, RpcSearcher)` — Initialize the caller. The built-in `JsonRpc_ResolveChain`/`JsonRpc_ResolveChainSet` methods must be registered separately (the caller only looks them up via `searcher`).

### Methods
- `BuildCandidateArgs(MethodCandidate, JsonElement[])` — Build the call-argument array for a single overload candidate, throwing if the JSON arguments don't fit.
- `CallCandidates(string, IList{MethodCandidate}, JsonElement[], RpcStageTimings)` — Try each overload candidate in order; invoke the first whose arguments deserialize successfully.
- `CallDirectWithObj(object, string, JsonElement[])` — Invoke a directly-registered method on a pre-deserialized instance. Called from chain resolution on the main thread.
- `Call(string, JsonElement[])` — Dispatch a registered RPC method by name with the given JSON arguments.
- `Call(string, JsonElement[], RpcStageTimings)` — Dispatch a registered RPC method by name with the given JSON arguments, optionally recording this call's stage breakdown (resolve/deserialize/invoke/serialize/overload attempts).
- `BuildInstanceCallArgs(object, JsonElement[], ParameterInfo[])` — Build the full argument array for an instance method call, filling defaults for omitted params.
- `DeserializeArgs(JsonElement[], ParameterInfo[])` — Deserialize a JSON argument array, filling defaults for omitted trailing parameters.
- `TryGetDefaultValue(ParameterInfo, Object@)` — Try to get the default value for a parameter from its attributes or compile-time default.
- `ResolveDefaultValue(object, Type)` — Convert `attrValue` to `targetType`, handling enums and type conversions.
- `JsonRpc_ResolveChain(RpcResolveChainParam)` — Resolve a chain of property accesses and a terminal method call on a Unity object, dispatching each step server-side. Registered as the `JsonRpc_ResolveChain` RPC method.
- `JsonRpc_ResolveChainSet(RpcResolveChainSetParam)` — Resolve a chain of property accesses on a Unity object and assign a value to the terminal property. Registered as the `JsonRpc_ResolveChainSet` RPC method.
- `ResolveStep(object, string, JsonElement[])` — Resolve a single step: try registered property getters first, then direct method dispatch.
- `DeserializeRoot(JsonElement)` — Deserialize the root Unity object descriptor into a live instance using its `rpcType`. When the descriptor also carries an `orgType` (the concrete .NET type name), that type is resolved and used instead of the coarse type registered for `rpcType`, so the deserialized value matches the sender's actual concrete type rather than the converter's declared base type.

---

## RpcType
*Namespace: LLiquidLink*

Represents a .NET Type reference transmitted as a JSON-RPC parameter.

---

## RpcInstanceObject
*Namespace: LLiquidLink*

JSON-serializable descriptor for a live Instance Object.

---

## RpcEnum
*Namespace: LLiquidLink*

Represents a .NET enum reference transmitted as a JSON-RPC parameter.

---

## RpcChainStep
*Namespace: LLiquidLink*

Single step in a property/method chain resolved server-side.

---

## PythonProcessManager
*Namespace: LLiquidLink*

Owns the lifecycle (start/kill) of the Python middleware process. Unity-independent.

### Constructors
- `__init__(Func{ILogger}, string, string, string)` — Initialize the manager and assemble the process start info.

### Properties
- `Process` — The most recently started Python process, or `null` if not started / already killed.

### Methods
- `Start` — Start the Python middleware process using the start info assembled in the constructor.
- `Kill` — Kill the Python process tree, if running. Swallows errors (best-effort cleanup).

---

## RpcOptions
*Namespace: LLiquidLink*

Options controlling bulk registration (AddRpcAll*) member enumeration.

### Properties
- `IncludeInherited` — When `true`, include members inherited from base types (except those declared on Object).
- `IncludeNested` — When `true`, recurse into public nested types as well as the type itself.

---

## RpcRegistrationTable
*Namespace: LLiquidLink*

Shared registration data written by RpcRegistry and read by RpcSearcher. Not exposed outside the assembly; access goes through the owning registrar/searcher instead.

### Methods
- `RpcTypeToOrgType` — Maps a wire-side `rpcType` name to its registered original .NET type.
- `RpcProperties` — Registered property getters, keyed by (owning type, property name); `null` type means root-level.
- `RpcSetProperties` — Registered property setters, keyed by (owning type, property name).
- `Router` — RPC method overload candidates, keyed by RPC name.
- `DirectEntries` — Direct-dispatch overload candidates, keyed by (instance type, method name).

---

## RpcRegistry
*Namespace: LLiquidLink*

Registers RPC methods, JSON converters, and property accessors, owning the registration table read by RpcSearcher.

### Constructors
- `__init__(Func{ILogger}, TypeResolver)` — Initialize the registrar.

### Properties
- `Searcher` — Read-only search interface over this registrar's registration table, used by MethodCaller for dispatch.
- `RegisteredRpcNames` — Names of all registered RPC handlers.

### Methods
- `AdditionalDefaultValueAttributeTypes` — Additional attribute types (beyond DefaultValueAttribute) that expose a public `Value` property, checked via reflection when resolving omitted RPC parameter defaults. Unity integrations (e.g. `UnityEngine.Internal.DefaultValueAttribute`) register their attribute type here at startup so MethodCaller never references UnityEngine directly. Only ever written once from a static constructor before any dispatch runs, so concurrent writes are not a concern.
- `Register(string, Delegate)` — Register a delegate under `rpcName`. Arguments are deserialized from JSON using the delegate's parameter types.
- `RegisterDirect(string, Type, string, ParameterInfo[], Func{Object[],Object}, string)` — Register a method for direct instance dispatch. The first JSON argument is deserialized as the instance; remaining arguments are matched to `methodParams`.
- `RegisterMethodOverloads(string, IList{MethodCandidate})` — Register one or more overload candidates under `rpcName`. On dispatch (via JsonElement[])), each candidate is tried in order and the first whose arguments deserialize successfully is invoked.
- `RegisterDirectMethodOverloads(string, string, IList{MethodCandidate})` — Register direct-dispatch overload candidates. Like MethodCandidate}), but instance candidates are also indexed by `methodName` so they can be reached via chain resolution.
- `AddConverterAndRegister``2(JsonSerializerOptions, RpcJsonConverter{``0,``1})` — Register a JSON converter that maps between an RPC wire type and its original .NET type. The converter is added to `options` and the RPC type map.
- `AddConverter(JsonSerializerOptions, JsonConverter)` — Register a converter factory on the fallback JSON options, tried when the primary serializer fails.
- `AddRpcMethod``1(``0, RpcOptions)` — Register a delegate as an RPC method. The RPC name is always derived from the delegate's declaring type and method name.
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
- `SaveRpcNamesCsv(string)` — Write all registered RPC method names to a CSV file (full_name, class_name, method_name).

---

## RpcSearcher
*Namespace: LLiquidLink*

Read-only lookup over the registration data written by RpcRegistry: resolves RPC method overloads, property accessors, and wire-type mappings for MethodCaller dispatch.

### Constructors
- `__init__(RpcRegistrationTable, TypeResolver)` — Initialize a searcher bound to a registration table and type resolver. Constructed once by RpcRegistry.

### Methods
- `TryGetCandidates(string, IList{MethodCandidate}@)` — Look up the overload candidates registered under `rpcName`.
- `TryGetDirectCandidates(Type, string, List{MethodCandidate}@)` — Look up the direct-dispatch overload candidates for `methodName` on `instanceType`.
- `TryGetRootProperty(string, ValueTuple{Type,Delegate}@)` — Look up a root-level (null-obj) property getter by name.
- `TryGetProperty(Type, string, ValueTuple{Type,Delegate}@)` — Look up a property getter for `t` by name.
- `TryGetSetProperty(Type, string, ValueTuple{Type,Action{Object,Object}}@)` — Look up a property setter for `t` by name.
- `TryResolveRpcType(string, Type@)` — Resolve a wire-side `rpcType` name to its registered original .NET type.
- `ResolveOrgType(string)` — Resolve a concrete .NET type name via the configured TypeResolver.

---

## MethodCandidate
*Namespace: LLiquidLink.RpcSearcher*

Describes one overload candidate of an RPC method: how to build its arguments and invoke it.

### Methods
- `IsStatic` — When `true`, all JSON arguments map to MethodParams and no instance argument is consumed.
- `InstanceType` — For instance methods, the type used to deserialize the first JSON argument (the instance).
- `MethodParams` — Parameter descriptors of the target method (excluding the instance parameter).
- `Method` — Invocation body: receives `[arg0, ...]` for static methods or `[instance, arg0, ...]` for instance methods.
- `FullName` — Fully qualified method name for logging.

---

## RpcStageTimings
*Namespace: LLiquidLink*

Accumulator for one RPC call's RpcStageTimings) stage breakdown, in seconds. Passed in by the caller (e.g. StdioTransport) and read back afterward; MethodCaller only ever writes to it, never reads it.

### Methods
- `ResolveTime` — Time spent resolving the RPC method name to overload candidates.
- `DeserializeArgsTime` — Time spent deserializing JSON arguments into call arguments, including failed overload attempts.
- `InvokeTime` — Time spent inside the resolved method itself (pure handler execution).
- `SerializeResultTime` — Time spent serializing the method's return value to a JsonElement.
- `OverloadAttempts` — Number of overload candidates whose argument deserialization was attempted.

---

## RpcTimingStats
*Namespace: LLiquidLink*

Tracks one stdio connection's RPC timing: request count, the local/communication/remote breakdown of total_time, wall-clock network transit times, per-stage breakdown of local processing, and wire byte volume. Mirrors the Python-side `RpcTimingStats` (`Python~/lliquidlink/core/_timing.py`) for the gateway<->Unity IPC leg; see that module's docstring for the shared `total_time = local_processing_time + communication_time + remote_time` model and the clock-skew cancellation argument. C# only ever plays the JSON-RPC server role on this leg (it never issues its own outbound request and awaits a reply), so !:RemoteTime is always 0.0 and !:CommunicationTime (derived, like the Python side) always equals !:RequestNetworkTime. !:BytesSent/!:BytesReceived count every frame that crosses the wire, including notifications, so they do not correlate 1:1 with !:RpcCount. A single instance is only ever touched from the main thread inside the transport's dispatch method (the "single event loop" equivalent of the Python design); it is not safe to share across threads.

### Constructors
- `__init__(bool)` — Create a stats tracker, initially with all counters at zero.

### Properties
- `Enabled` — When false, Double)/Double)/Int64) are no-ops.

### Methods
- `Record(double, double, double, double)` — Add one completed request/response cycle's timing to the running totals; no-op when disabled.
- `RecordStages(RpcStageTimings, double, double, double, double)` — Add one request's stage breakdown to the running totals; no-op when disabled. Additive only (does not touch !:RpcCount, which Double) owns).
- `RecordBytes(long, long)` — Add wire byte counts to the running totals; no-op when disabled. Does not touch !:RpcCount: called for every frame on the wire (including notifications), not just completed request/response cycles.
- `Reset` — Clear accumulated totals for a new connection; keeps Enabled.
- `ToDict` — Return the current stats as a plain dict, key-compatible with the Python side's `to_dict()`, plus this leg's stage breakdown. `communication_time` is derived (`request_network_time + response_network_time`), not stored, matching the Python side.

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
- `WorkingDirectory` — Path to the directory, used to locate python server.
- `Rpc` — Registrar used to add RPC methods, converters, and property accessors.
- `Logger`
- `IsRunning` — True while the server is actively listening for connections.
- `TypeResolver` — The TypeResolver used to resolve .NET type names from RPC parameters.
- `JsonChain` — The pre/main/fallback JSON serializer chain, needed by external callers registering converters via RpcJsonConverter{``0,``1}), AddPreConverter<TOrg, TRpc>, or JsonConverter).
- `OnError`

### Methods
- `RaiseOnDisconnect(int)` — Raise OnDisconnect. Field-like events can only be invoked from their declaring type, so subclasses use this instead.
- `CreateDefaultLogger` — Create the logger used when no other logger has been assigned. Override to supply a host-specific logger.
- `AddConverters(JsonSerializerOptions[])` — Register the core pre/main-stage converters shared by every host.
- `Finalize` — Safety net for a caller who never called Stop: kill the child Python process so it does not outlive this object. Deliberately does not call Stop, which touches managed objects (transport, dispatcher, logger) that may already be finalized or, for `_dispatcher`, require the Unity main thread that the finalizer thread is not.
- `WireTransportEvents` — Subscribe to transport events, shared by every transport (stdio or injected).
- `BuildCoreStack` — Build the shared RPC core (serializer options, method caller, registrar, registries, converters) used by both constructors. Transport wiring is left to each constructor.
- `Stop` — Stop the server and disconnect all clients.
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

## IMainThreadDispatcher
*Namespace: LLiquidLink*

Abstraction for dispatching actions onto Unity's main thread.

### Methods
- `Enqueue(Action)` — Enqueue `action` to be executed on the main thread.
- `Start` — Start draining the action queue on the main thread.
- `Stop` — Stop draining the queue and unregister from the main-thread update callback.

---

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

## StdioTransport
*Namespace: LLiquidLink*

Reads JSON-RPC requests from the Python middleware's stdio streams with 4-byte little-endian length framing, dispatches via MethodCaller, and writes responses.

### Constructors
- `__init__(IMainThreadDispatcher, MethodCaller, JsonSerializerOptions, Func{ILogger})` — Initialize StdioTransport with its dependencies. Call Stream) before Start.

### Properties
- `Timing` — RPC timing/byte-volume stats for this stdio connection (gateway leg of the IPC hop).
- `ClientId`

### Methods
- `AttachStreams(Stream, Stream, Stream)` — Attach the Python middleware's stdio streams. Must be called once before Start.
- `Start` — Start the read loop on a background thread and fire OnConnect.
- `Stop` — Signal the read loop to stop and release its thread/stream resources. Closing the streams unblocks the background threads' blocking reads so they can exit and be joined.
- `SendAll(ArraySegment{Byte})` — Write `data` to the Python middleware's stdin (there is only ever one stdio client).

### Events
- `OnConnect`
- `OnDisconnect`
- `OnData`
- `OnError`

---

## JsonUtilityConverterFactory
*Namespace: UniLiquidLink*

Routes matching value types to JsonUtility so System.Text.Json can compose them inside collections.

---

## JsonUtilityConverter`1
*Namespace: UniLiquidLink*

Delegates single-value (de)serialization to Unity's JsonUtility, bridging System.Text.Json tokens via a raw JSON string.

---

## UnityObjectNullChecker
*Namespace: UniLiquidLink*

Null checker for Object values, meant to be injected into UnityObjectConverter. Uses Unity's overloaded `==`, so destroyed objects and the "fake null" returned by e.g. `GetComponent` for a missing component count as null although they are not reference-null.

### Methods
- `IsNull(Object)` — Whether `value` is a destroyed or fake-null Unity object.

---

## InstanceObjectConverter`1
*Namespace: UniLiquidLink*

Converter that maps live object instances to RpcInstanceObject descriptors via the shared registry. An injected IRpcNullChecker`1 can widen what is written as JSON null.

### Constructors
- `__init__(ObjectRegistry, IRpcNullChecker{`0})` — Initialize the converter with its registry and an optional null checker.

### Methods
- `_nullChecker` — Optional injected null definition; `null` means only reference-null is written as JSON null.

---

## MainThreadDispatcher
*Namespace: UniLiquidLink*

Drains an action queue on Unity's main thread. Registered with update, but once a drain starts it keeps hold of the main thread for as long as new actions keep arriving (bounded by IdleWaitMilliseconds) instead of handling one tick's worth and yielding. This removes the per-RPC wait for the next Editor tick during a burst of sequential RPC traffic.

### Properties
- `IdleWaitMilliseconds` — How long a drain keeps the main thread waiting for the next action before yielding back to the Editor. The RPC peer turns a response around in roughly 1.5ms, so any value above that keeps a whole burst inside a single drain; the Editor stays frozen for that burst and recovers this long after traffic stops. Set to 0 to restore the previous one-drain-per-tick behaviour.

### Methods
- `Enqueue(Action)` — Enqueue `action` for execution on the main thread.
- `Start` — Register the drain loop with update.
- `Stop` — Unregister the drain loop from update.
- `ProcessAll` — Dequeue and invoke every pending action, then hold the main thread here waiting up to IdleWaitMilliseconds for the next one, repeating until that wait times out. Each action is isolated so one throwing does not stop the rest. Returns immediately when the queue is empty (an idle Editor pays nothing) or when a drain is already running on this thread.

---

## Server
*Namespace: UniLiquidLink*

Unity-specific entry point for the WebSocket-RPC stack; adds Python-middleware process management, Unity object registration, and Unity-flavored logging on top of the Unity-independent Server base class.

### Constructors
- `__init__(string)` — Production constructor; starts Python middleware and communicates via stdio.
- `__init__(string, ITransportServer, IMainThreadDispatcher)` — Injection constructor for unit tests: accepts a pre-wired transport and dispatcher.

### Properties
- `DispatcherIdleWaitMilliseconds` — How long the main-thread drain waits for the next RPC before yielding back to the Editor (forwards to IdleWaitMilliseconds). Raising it keeps the drain alive across client-side pauses longer than the default, at the cost of the Editor staying frozen that much longer after traffic stops. Reads 0 and ignores writes when the injected dispatcher is not a MainThreadDispatcher.

### Methods
- `CreateDefaultLogger`
- `Start` — Start the server, launching the Python middleware first when running in stdio mode.
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

## UnityObjectConverter
*Namespace: UniLiquidLink*

JSON converter for the base Object type, via the shared registry-lookup logic in InstanceObjectConverter`1. The injected IRpcNullChecker`1 (normally UnityObjectNullChecker) makes destroyed / fake-null objects (e.g. the result of `GetComponent` for a missing component) be written as JSON null instead of an object descriptor.

### Constructors
- `__init__(ObjectRegistry, IRpcNullChecker{Object})` — Initialize the converter with the registry used for instance ID lookups and the null definition to apply.

---
