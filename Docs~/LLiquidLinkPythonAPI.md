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
- `__init__(stream: ByteStream, default: Optional[Callable[[Any], Any]]=None, object_hook: Optional[Callable[[Dict[str, Any]], Any]]=None)`

### Properties
- `closed`

### Methods
- `request(method: str, params: List[Any])` — Send a request and await its response.
- `notify(method: str, params: List[Any])` — Send a fire-and-forget notification (no id).
- `serve()` — Receive loop: resolve pending requests; ignore inbound notifications.
- `aclose()`

---

## StdioByteStream
*Module: lliquidlink.core._rpc*

Byte stream over blocking stdin/stdout, read in a worker thread.

### Constructors
- `__init__(reader: Optional[BinaryIO]=None, writer: Optional[BinaryIO]=None)`

### Methods
- `receive(max_bytes: int=65536)`
- `send(data: bytes)`
- `aclose()`

---

## Functions (lliquidlink.core._rpc)

- `encode_frame(msg: Dict[str, Any], default: Optional[Callable[[Any], Any]]=None)` — Encode a message as a 4-byte little-endian length prefix + JSON body.
- `decode_frame(raw: Union[bytes, str])` — Decode a single length-prefixed frame (where one message == one frame).

---

## ByteStream
*Module: lliquidlink.core._interfaces*

Bidirectional byte stream: anything with async send/receive/aclose.

### Methods
- `send(data: bytes)`
- `receive(max_bytes: int=...)`
- `aclose()`

---

## Functions (lliquidlink.core._logging)

- `resolve_configured_level(logger: logging.Logger, default: int)` — Return the nearest explicit (non-NOTSET) level already configured on

---

## Client
*Module: lliquidlink.client._client*

Client that connects to a Unity Editor server and sends RPC commands.

### Constructors
- `__init__(transport: Transport, verify_releases: bool=False)`

### Properties
- `is_running`

### Methods
- `__getattr__(name: str)` — Treat any undefined non-underscore attribute as a Unity RPC method.
- `flush_releases()` — Send pending object releases now (callable from a worker thread).
- `mainloop()` — Connect, run on_execute in a worker thread, flush, then disconnect.
- `connect()` — Open the connection to the Unity server.
- `disconnect()` — Close the connection to the Unity server.
- `execute(on_execute: Callable[[Client], None])` — Run a callback in a worker thread with this client as its argument.
- `add_abbreviated_classes(class_names: List[str])` — Register a class name whose methods can be called without namespace prefix.
- `add_abbreviated_namespaces(namespaces: List[str])` — Register namespaces whose types can be referred to by simple name.

---

## ObjectProxy
*Module: lliquidlink.client._proxy*

Proxy for a live Unity object; attribute access builds RPC chains.

### Constructors
- `__init__(data: Dict[str, Any], transport: Transport, track: Callable[[ObjectProxy, Dict[str, Any]], None], make_property_proxy: Callable[[Dict[str, Any], List[str]], PropertyProxy])`

### Methods
- `__getattr__(name: str)`
- `__setattr__(name: str, value)`
- `__repr__()`

---

## PropertyProxy
*Module: lliquidlink.client._proxy*

Accumulates a property/method chain, resolved server-side when called.

### Constructors
- `__init__(obj: Optional[Dict[str, Any]], chain: List[str], transport: Transport, make_property_proxy: Callable[[Dict[str, Any], List[str]], PropertyProxy])`

### Methods
- `__getattr__(name: str)`
- `__setattr__(name: str, value)`
- `__call__(*args)` — Resolve synchronously via the transport's call_sync bridge.
- `__await__()`

---

## Serialization
*Module: lliquidlink.client._serialization*

Namespace for the json.dumps default and json.loads object_hook factories.

### Constructors
- `__init__(make_object_proxy: Callable[[Dict[str, Any]], Any])`

### Methods
- `encode_default(obj)` — json.dumps default: dataclass -> shallow field dict; any other object
- `object_hook(d: Dict[str, Any])` — Build a json.loads object_hook: InstanceObject dict -> ObjectProxy.

---

## StreamTransport
*Module: lliquidlink.client._transports*

Base transport: opens a byte stream and runs a JsonRpcPeer over it.

### Constructors
- `__init__(open: Callable[[], Awaitable[ByteStream]])`

### Properties
- `closed`

### Methods
- `bind_codec(serialization: Serialization)` — Set the json.dumps default / json.loads object_hook used by the peer.
- `open()`
- `aclose()`
- `rpc_call(method: str, params: List[Any])`
- `rpc_notify(method: str, params: List[Any])`
- `call_sync(method: str, params: List[Any])`
- `run(coro: Coroutine[Any, Any, Any])` — Run a coroutine synchronously from a worker thread, else return it.

---

## Functions (lliquidlink.client._transports)

- `StdioJsonRpcTransport()` — JSON-RPC transport over this process's stdin/stdout.
- `TcpJsonRpcTransport(host: str, port: int)` — JSON-RPC transport over a raw TCP socket.

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
- `__iadd__(handler: Callable[..., None])`
- `__isub__(handler: Callable[..., None])`
- `__call__(*args, **kwargs)`

---

## Transport
*Module: lliquidlink.client._interfaces*

RPC transport shape used by Client/ObjectProxy/PropertyProxy/ReleaseManager.

### Properties
- `closed`

### Methods
- `bind_codec(serialization: Serialization)`
- `open()`
- `aclose()`
- `rpc_call(method: str, params: List[Any])`
- `rpc_notify(method: str, params: List[Any])`
- `call_sync(method: str, params: List[Any])`
- `run(coro: Coroutine[Any, Any, Any])`

---

## SupportsAsDict
*Module: lliquidlink.client._interfaces*

Anything exposing `_asdict()` for wire encoding (e.g. ObjectProxy).

---

## ReleaseManager
*Module: lliquidlink.client._release*

Tracks GC'd ObjectProxy instances and batches their release over RPC.

### Constructors
- `__init__(transport: Transport, verify: bool=False)`

### Methods
- `track(proxy, data: Dict[str, Any])` — Schedule a release when `proxy` is garbage-collected.
- `flush_async()`
- `flush()` — Send pending object releases now (callable from a worker thread).

---

## Functions (lliquidlink.client._release)

- `gc_flush(func: Callable[..., Any])` — Decorator: flush pending Unity object releases after the method returns.

---

## RpcType
*Module: lliquidlink.client._schema*

Represents a .NET Type reference transmitted as a JSON-RPC parameter.

---

## RpcChainStep
*Module: lliquidlink.client._schema*

Single step in a property/method chain resolved server-side.

---

## RpcEnum
*Module: lliquidlink.client._schema*

Represents a .NET enum reference transmitted as a JSON-RPC parameter.

---

## RpcResolveChainParam
*Module: lliquidlink.client._schema*

---

## RpcResolveChainSetParam
*Module: lliquidlink.client._schema*

---

## IpcBridge
*Module: lliquidlink.server.ipc_bridge*

Talks to the C# Unity process over stdio using the shared JSON-RPC core.

### Constructors
- `__init__(stream: Optional[ByteStream]=None)`

### Methods
- `connect()` — Start the background loop that reads C# responses from stdin.
- `call(method: str, params: List[Any])` — Send a JSON-RPC request to C# and await the response.
- `anotify(method: str, params: List[Any])` — Send a JSON-RPC notification to C# and await delivery.

---

## RpcNameResolver
*Module: lliquidlink.server.resolver*

Maps (class_name, method_name) pairs to full RPC names loaded from a CSV file.

### Constructors
- `__init__(csv_path: str)`

### Methods
- `add_abbreviated_classes(class_names: Union[str, List[str]])` — Register a class whose unqualified method names are resolved to full RPC names.
- `try_resolve(name: str)` — Return the full RPC name for name under any registered abbreviated class.
- `resolve(method: str)` — Return the full RPC name if method matches an abbreviated class, else return method unchanged.
- `resolve_chain_params(params: List[Any])` — Resolve the first chain step (params[0]['steps'][0]['name']) of a ResolveChain* call.
- `try_resolve_class_method(class_name: str, method: str)` — Return the full RPC name registered for (class_name, method), or None.
- `try_collapse_static_chain(method: str, params: List[Any])` — Collapse a root JsonRpc_ResolveChain whose last step is a static class.

---

## TypeNameResolver
*Module: lliquidlink.server.resolver*

Resolves short .NET type names to their FullName using a CSV of allowed types.

### Constructors
- `__init__(csv_path: str)`

### Methods
- `add_abbreviated_namespaces(namespaces: Union[str, List[str]])` — Register namespaces whose types can be referred to by simple name.
- `resolve(name: str)` — Resolve a .NET type name to its FullName.

---

## ServerTransport
*Module: lliquidlink.server._transport*

Base transport: accepts external client connections and dispatches each to a handler.

### Methods
- `serve(handler: Callable[[MessageStream], Awaitable[None]])` — Start accepting connections, invoking handler(connection) for each. Runs until stop().
- `stop()` — Signal serve() to return.

---

## TcpServerTransport
*Module: lliquidlink.server._transport*

Default ServerTransport: a raw TCP listener, one connection per client.

### Constructors
- `__init__(host: str='localhost', port: int=8700)`

### Methods
- `serve(handler: Callable[[MessageStream], Awaitable[None]])`
- `stop()`

---

## Server
*Module: lliquidlink.server.server*

Owns both legs of the bridge: the external ServerTransport and the C# IpcBridge.

### Constructors
- `__init__(data_dir: str, transport: ServerTransport, bridge: Optional[IpcBridge]=None, rpc_names_csv: str='rpc_names.csv', type_names_csv: str='type_names.csv', resolver: Optional[RpcNameResolver]=None, type_resolver: Optional[TypeNameResolver]=None)`

### Methods
- `serve()`
- `stop()`

---

## MessageStream
*Module: lliquidlink.server._interfaces*

Async-iterable of raw frames plus send, used by `_handle_client`.

### Methods
- `__aiter__()`
- `__anext__()`
- `send(data: bytes)`

---

## RpcBridge
*Module: lliquidlink.server._interfaces*

The `_handle_client` side of the C# IPC bridge: call/anotify only.

### Methods
- `call(method: str, params: List[Any])`
- `anotify(method: str, params: List[Any])`

---
