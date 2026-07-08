# LLiquidLink Python API Summary

## ConnectionClosedError
*Module: lliquidlink.core._rpc*

Raised when an RPC is attempted on a closed connection.

---

## RpcError
*Module: lliquidlink.core._rpc*

Raised when the server returns a JSON-RPC error response.

---

## JsonRpcPeer
*Module: lliquidlink.core._rpc*

JSON-RPC 2.0 over a bidirectional byte stream with 4-byte length framing.

### Constructors
- `__init__(stream, default=None, object_hook=None)`

### Properties
- `closed`

### Methods
- `request(method: str, params: list)` — Send a request and await its response.
- `notify(method: str, params: list)` — Send a fire-and-forget notification (no id).
- `serve()` — Receive loop: resolve pending requests; ignore inbound notifications.
- `aclose()`

---

## StdioByteStream
*Module: lliquidlink.core._rpc*

Byte stream over blocking stdin/stdout, read in a worker thread.

### Constructors
- `__init__(reader=None, writer=None)`

### Methods
- `receive(max_bytes: int=65536)`
- `send(data: bytes)`
- `aclose()`

---

## Functions (lliquidlink.core._rpc)

- `encode_frame(msg: dict, default=None)` — Encode a message as a 4-byte big-endian length prefix + JSON body.
- `decode_frame(raw)` — Decode a single length-prefixed frame (where one message == one frame).

---

## Client
*Module: lliquidlink.client._client*

Client that connects to a Unity Editor server and sends RPC commands.

### Constructors
- `__init__(transport: StreamTransport)`

### Properties
- `is_running`

### Methods
- `__getattr__(name: str)` — Treat any undefined non-underscore attribute as a Unity RPC method.
- `flush_releases()` — Send pending object releases now (callable from a worker thread).
- `mainloop()` — Connect, run on_execute in a worker thread, flush, then disconnect.
- `connect()` — Open the connection to the Unity server.
- `disconnect()` — Close the connection to the Unity server.
- `execute(on_execute)` — Run a callback in a worker thread with this client as its argument.
- `add_abbreviated_classes(class_names: List[str])` — Register a class name whose methods can be called without namespace prefix.
- `add_abbreviated_namespaces(namespaces: List[str])` — Register namespaces whose types can be referred to by simple name.

---

## Functions (lliquidlink.client._client)

- `gc_flush(func)` — Decorator: flush pending Unity object releases after the method returns.

---

## ObjectProxy
*Module: lliquidlink.client._proxy*

Proxy for a live Unity object; attribute access builds RPC chains.

### Constructors
- `__init__(client, data: dict)`

### Methods
- `__getattr__(name: str)`
- `__setattr__(name: str, value: Any)`
- `__repr__()`

---

## PropertyProxy
*Module: lliquidlink.client._proxy*

Accumulates a property/method chain, resolved server-side when called.

### Constructors
- `__init__(client, obj, chain: List[str])`

### Methods
- `__getattr__(name: str)`
- `__setattr__(name: str, value: Any)`
- `__call__(*args)` — Resolve synchronously (worker thread) or return a coroutine (async).
- `__await__()`

---

## Serialization
*Module: lliquidlink.client._serialization*

Namespace for the json.dumps default and json.loads object_hook factories.

### Constructors
- `__init__(make_object_proxy)`

### Methods
- `encode_default(obj: Any)` — json.dumps default: ObjectProxy -> raw dict; dataclass -> dict.
- `object_hook(d: dict)` — Build a json.loads object_hook: InstanceObject dict -> ObjectProxy.

---

## StreamTransport
*Module: lliquidlink.client._transports*

Base transport: opens a byte stream and runs a JsonRpcPeer over it.

### Constructors
- `__init__()`

### Properties
- `closed`

### Methods
- `bind_codec(serialization: Serialization)` — Set the json.dumps default / json.loads object_hook used by the peer.
- `open()`
- `aclose()`
- `rpc_call(method, params)`
- `rpc_notify(method, params)`

---

## StdioJsonRpcTransport
*Module: lliquidlink.client._transports*

JSON-RPC transport over this process's stdin/stdout.

---

## TcpJsonRpcTransport
*Module: lliquidlink.client._transports*

JSON-RPC transport over a raw TCP socket.

### Constructors
- `__init__(host: str, port: int)`

---

## Functions (lliquidlink.client.models)

- `type_(value: str)` — Create an RpcType parameter for passing a .NET Type to a Unity RPC method.

---

## Event
*Module: lliquidlink.client._event*

A list of callbacks invoked in registration order when called.

### Constructors
- `__init__()`

### Methods
- `__iadd__(handler: Callable)`
- `__isub__(handler: Callable)`
- `__call__(*args, **kwargs)`

---

## RpcType
*Module: lliquidlink.client._schema*

Represents a .NET Type reference transmitted as a JSON-RPC parameter.

---

## RpcChainStep
*Module: lliquidlink.client._schema*

Single step in a property/method chain resolved server-side.

---

## ReleaseRequest
*Module: lliquidlink.client._schema*

Batch request to release Unity object references held by the server.

---

## RpcEnum
*Module: lliquidlink.client._schema*

Represents a .NET Type reference transmitted as a JSON-RPC parameter.

---

## IpcBridge
*Module: lliquidlink.server.ipc_bridge*

Talks to the C# Unity process over stdio using the shared JSON-RPC core.

### Constructors
- `__init__(stream=None)`

### Methods
- `connect()` — Start the background loop that reads C# responses from stdin.
- `call(method: str, params: list)` — Send a JSON-RPC request to C# and await the response.
- `notify(method: str, params: list)` — Send a JSON-RPC notification to C# (fire-and-forget, no id).
- `anotify(method: str, params: list)` — Send a JSON-RPC notification to C# and await delivery.

---

## RpcNameResolver
*Module: lliquidlink.server.resolver*

Maps (class_name, method_name) pairs to full RPC names loaded from a CSV file.

### Constructors
- `__init__(csv_path: str)`

### Methods
- `add_abbreviated_classes(class_names)` — Register a class whose unqualified method names are resolved to full RPC names.
- `try_resolve(name: str)` — Return the full RPC name for name under any registered abbreviated class.
- `resolve(method: str)` — Return the full RPC name if method matches an abbreviated class, else return method unchanged.
- `resolve_chain_params(params: list)` — Resolve the first chain step (params[1][0]['name']) of a ResolveChain* call.
- `try_resolve_class_method(class_name: str, method: str)` — Return the full RPC name registered for (class_name, method), or None.
- `try_collapse_static_chain(method: str, params: list)` — Collapse a root JsonRpc_ResolveChain whose last step is a static class.

---

## TypeNameResolver
*Module: lliquidlink.server.resolver*

Resolves short .NET type names to their FullName using a CSV of allowed types.

### Constructors
- `__init__(csv_path: str)`

### Methods
- `add_abbreviated_namespaces(namespaces)` — Register namespaces whose types can be referred to by simple name.
- `resolve(name: str)` — Resolve a .NET type name to its FullName.

---

## ServerTransport
*Module: lliquidlink.server._transport*

Base transport: accepts external client connections and dispatches each to a handler.

### Methods
- `serve(handler)` — Start accepting connections, invoking handler(connection) for each. Runs until stop().
- `stop()` — Signal serve() to return.

---

## TcpServerTransport
*Module: lliquidlink.server._transport*

Default ServerTransport: a raw TCP listener, one connection per client.

### Constructors
- `__init__(host: str='localhost', port: int=8700)`

### Methods
- `serve(handler)`
- `stop()`

---

## Server
*Module: lliquidlink.server.server*

Owns both legs of the bridge: the external ServerTransport and the C# IpcBridge.

### Constructors
- `__init__(transport: ServerTransport, bridge: IpcBridge=None, rpc_names_csv: str='rpc_names.csv', type_names_csv: str='type_names.csv', resolver: RpcNameResolver=None, type_resolver: TypeNameResolver=None)`

### Methods
- `serve()`
- `stop()`

---
