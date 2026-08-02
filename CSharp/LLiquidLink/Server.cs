using LLiquidLink.Logger;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

namespace LLiquidLink
{
    /// <summary>Public interface for an RPC executor server.</summary>
    public interface IExecutorServer
    {
        /// <summary>Logger instance used for diagnostic output.</summary>
        ILogger Logger { get; set; }

        /// <summary>Callback invoked when a transport-level or RPC error occurs.</summary>
        Action<Exception> OnError { get; set; }
    }

    /// <summary>
    /// Assembles and owns the Unity-independent half of the WebSocket-RPC stack: transport wiring,
    /// serializer chain, object/type registries, and RPC dispatch. Host-specific concerns (default
    /// converters, default logger, working-directory resolution) are exposed as virtual hooks for a
    /// subclass (e.g. <c>UniLiquidLink.Server</c>) to fill in.
    /// </summary>
    public class Server : IExecutorServer
    {
        protected readonly ITransportServer _transport;
        protected PythonProcessManager _pythonProcessManager;
        protected readonly IMainThreadDispatcher _dispatcher;
        protected readonly List<int> _connectedClients = new List<int>();
        protected static readonly string ServerDir = Utils.GetCurrentDirectory();
        protected JsonSerializerChain _jsonChain;

        /// <summary>Path to the directory, used to locate python server.</summary>
        public string WorkingDirectory { get; set; }

        internal ObjectRegistry _registry;
        internal TypeResolver _typeResolver;

        /// <summary>Registrar used to add RPC methods, converters, and property accessors.</summary>
        public RpcRegistry Rpc { get; private set; }
        public ObjectRegistry Registry => _registry;

        /// <summary>Fired when a client disconnects. Parameter: client ID.</summary>
        public event Action<int> OnDisconnect;

        /// <summary>Fired when the Python server reports a startup or runtime error.</summary>
        public event Action<string> OnServerError;

        /// <summary>Raise <see cref="OnDisconnect"/>. Field-like events can only be invoked from their declaring type, so subclasses use this instead.</summary>
        /// <param name="clientId">Client ID passed to subscribers.</param>
        protected void RaiseOnDisconnect(int clientId)
        {
            OnDisconnect?.Invoke(clientId);
        }

        /// <summary>No-op logger that silently discards all messages.</summary>
        public class NullLogger : ILogger
        {
            /// <inheritdoc/>
            public LogLevel MinLevel { get; set; }

            /// <inheritdoc/>
            public void Debug(string msg) { }

            /// <inheritdoc/>
            public void Info(string msg) { }

            /// <inheritdoc/>
            public void InfoFormat(string format, params object[] args) { }

            /// <inheritdoc/>
            public void DebugFormat(string format, params object[] args) { }
        }

        /// <inheritdoc/>
        public ILogger Logger { get; set; }

        /// <summary>Create the logger used when no other logger has been assigned. Override to supply a host-specific logger.</summary>
        protected virtual ILogger CreateDefaultLogger()
        {
            return new NullLogger();
        }

        /// <summary>Register the core pre/main-stage converters shared by every host.</summary>
        private void AddConverters(JsonSerializerOptions[] options) {
            JsonSerializerOptions mainOptions = options[(int)JsonSerializerChain.Stage.Main];
            JsonSerializerOptions preOptions = options[(int)JsonSerializerChain.Stage.Pre];
            Rpc.AddConverterAndRegister(mainOptions, new TypeConverter(_typeResolver));
            Rpc.AddConverterAndRegister(mainOptions, new EnumConverter());
            Rpc.AddConverter(preOptions, new PreObjectConverter(_registry));
            Rpc.AddConverterAndRegister(mainOptions, new ObjectPrimitiveConverter());
        }

        protected virtual void Initialize() { }

        /// <summary>Stdio-transport constructor; starts Python middleware and communicates via stdio.</summary>
        /// <param name="dispatcher">Main-thread dispatcher implementation supplied by the host.</param>
        protected Server(IMainThreadDispatcher dispatcher)
        {
            Logger = CreateDefaultLogger();
            _dispatcher = dispatcher;
            MethodCaller caller = BuildCoreStack();
            _transport = new StdioTransport(dispatcher, caller, _jsonChain.Options[(int)JsonSerializerChain.Stage.Main], () => Logger);
            Rpc.Register("OnServerError", (Action<string>)(msg =>
            {
                Logger.Info("Python server error: " + msg);
                OnServerError?.Invoke(msg);
            }));
            WireTransportEvents();
            Initialize();
        }

        /// <summary>
        /// Safety net for a caller who never called <see cref="Stop"/>: kill the child Python process so it
        /// does not outlive this object. Deliberately does not call <see cref="Stop"/>, which touches managed
        /// objects (transport, dispatcher, logger) that may already be finalized or, for <c>_dispatcher</c>,
        /// require the Unity main thread that the finalizer thread is not.
        /// </summary>
        ~Server()
        {
            if (_pythonProcessManager != null)
            {
                _pythonProcessManager.Kill();
            }
        }

        /// <summary>Injection constructor for unit tests: accepts a pre-wired transport and dispatcher.</summary>
        /// <param name="transport">Stub transport (e.g. NullTransport) for testing.</param>
        /// <param name="dispatcher">Stub dispatcher for testing.</param>
        protected Server(ITransportServer transport, IMainThreadDispatcher dispatcher)
        {
            Logger = CreateDefaultLogger();
            _dispatcher = dispatcher;
            BuildCoreStack();
            _transport = transport;
            WireTransportEvents();
            Initialize();
        }

        /// <summary>Subscribe to transport events, shared by every transport (stdio or injected).</summary>
        void WireTransportEvents()
        {
            _transport.OnConnect += (id, ep) => { _connectedClients.Add(id); Logger.Info("Client connected (id=" + id + ", endpoint=" + ep + ")"); };
            _transport.OnDisconnect += id => { _connectedClients.Remove(id); Logger.Info("Client disconnected (id=" + id + ")"); RaiseOnDisconnect(id); };
            _transport.OnError += (id, ex) => { Logger.Info("Transport error (id=" + id + "): " + (ex != null ? ex.Message : "")); OnError?.Invoke(ex); };
        }

        /// <summary>
        /// Build the shared RPC core (serializer options, method caller, registrar, registries, converters)
        /// used by both constructors. Transport wiring is left to each constructor.
        /// </summary>
        /// <returns>The configured <see cref="MethodCaller"/>.</returns>
        protected MethodCaller BuildCoreStack()
        {
            var chain = new JsonSerializerChain();
            _jsonChain = chain;

            _registry = new ObjectRegistry(() => Logger);
            _typeResolver = new TypeResolver(() => Logger);
            Rpc = new RpcRegistry(() => Logger, _typeResolver);
            var caller = new MethodCaller(() => Logger, chain, Rpc.Searcher);
            Rpc.Register("JsonRpc_ResolveChain",
                (Func<RpcResolveChainParam, object>)caller.JsonRpc_ResolveChain);
            Rpc.Register("JsonRpc_ResolveChainSet",
                (Func<RpcResolveChainSetParam, object>)caller.JsonRpc_ResolveChainSet);
            Rpc.Register("JsonRpc_ReleaseObjects",
                (Func<long[], List<long>>)_registry.RemoveObjects);
            AddConverters(chain.Options);
            return caller;
        }

        // ─── Lifecycle ───────────────────────────────────────────────────────────

        /// <summary>Stop the server and disconnect all clients.</summary>
        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            if (_pythonProcessManager != null)
            {
                _pythonProcessManager.Kill();
            }
            _transport.Stop();
            _dispatcher.Stop();
            _connectedClients.Clear();
            IsRunning = false;
            Logger.Info("Server stopped");

            // The Python process was already killed above, so the finalizer's safety net is no
            // longer needed; skip it to avoid an unnecessary finalization queue entry.
            GC.SuppressFinalize(this);
        }

        /// <summary>True while the server is actively listening for connections.</summary>
        public bool IsRunning { get; protected set; }

        // ─── Event push to Python ────────────────────────────────────────────────

        /// <summary>Push a named event with optional payload to all connected Python clients.</summary>
        /// <param name="eventType">Event name string.</param>
        /// <param name="data">Optional key-value payload dictionary.</param>
        public void SendEvent(string eventType, Dictionary<string, object> data = null)
        {
            if (!IsRunning || _connectedClients.Count == 0)
            {
                return;
            }

            var message = new Dictionary<string, object>
            {
                { "action", "event" },
                { "event",  eventType },
                { "data",   data ?? new Dictionary<string, object>() }
            };
            byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
            _transport.SendAll(new ArraySegment<byte>(bytes));
        }

        /// <summary>The <see cref="TypeResolver"/> used to resolve .NET type names from RPC parameters.</summary>
        public TypeResolver TypeResolver => _typeResolver;

        /// <summary>The pre/main/fallback JSON serializer chain, needed by external callers registering converters
        /// via <see cref="RpcRegistry.AddConverterAndRegister{TOrg, TRpc}"/>, <see cref="RpcRegistry.AddPreConverter{TOrg, TRpc}"/>,
        /// or <see cref="RpcRegistry.AddConverter"/>.</summary>
        public JsonSerializerChain JsonChain => _jsonChain;

        /// <summary>
        /// Register the assembly of the direct caller and all referenced assemblies for type resolution.
        /// Must not be inlined so the calling assembly is detected correctly.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void RegisterCallerAssembly()
        {
            _typeResolver.RegisterAssembly(Assembly.GetCallingAssembly());
        }

        /// <inheritdoc/>
        public Action<Exception> OnError { get; set; }
    }
}
