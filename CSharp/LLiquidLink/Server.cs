using LLiquidLink.Logger;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

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

    public class ConverterOnlyResolver : IJsonTypeInfoResolver
    {
        private readonly IJsonTypeInfoResolver _inner = new DefaultJsonTypeInfoResolver();

        public JsonTypeInfo? GetTypeInfo(Type type, JsonSerializerOptions options)
        {
            bool hasRegisteredConverter = options.Converters.Any(c => c.CanConvert(type));

            return !hasRegisteredConverter ? throw new NotSupportedException(type.FullName) : _inner.GetTypeInfo(type, options);
        }
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
        protected readonly StdioTransport _stdioTransport;
        protected Process _pythonProcess;
        protected readonly IMainThreadDispatcher _dispatcher;
        protected readonly List<int> _connectedClients = new List<int>();

        /// <summary>Path to the directory, used to locate python server.</summary>
        public string WorkingDirectory { get; set; }

        internal ObjectRegistry _registry;
        internal TypeResolver _typeResolver;

        /// <summary>Registrar used to add RPC methods, converters, and property accessors.</summary>
        public RpcRegistrar Rpc { get; private set; }
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

        /// <summary>Register additional main-stage converters. Override to add host-specific converters.</summary>
        protected virtual void AddConverters() { }

        /// <summary>Register additional fallback-stage converter factories. Override to add host-specific converters.</summary>
        protected virtual void AddFallbackConverters() { }

        /// <summary>Stdio-transport constructor; starts Python middleware and communicates via stdio.</summary>
        /// <param name="dispatcher">Main-thread dispatcher implementation supplied by the host.</param>
        protected Server(IMainThreadDispatcher dispatcher)
        {
            Logger = CreateDefaultLogger();
            _dispatcher = dispatcher;
            var bus = BuildCoreStack(out JsonSerializerOptions jsonOptions);
            _stdioTransport = new StdioTransport(_dispatcher, bus, jsonOptions, () => Logger, ex => OnError?.Invoke(ex));
            _stdioTransport.OnConnect += id => { _connectedClients.Add(id); Logger.Info("Python middleware connected"); };
            _stdioTransport.OnDisconnect += id => { 
                _connectedClients.Remove(id); 
                Logger.Info("Python middleware disconnected"); 
                OnDisconnect?.Invoke(id); 
            };
            bus.Register("OnServerError", (Action<string>)(msg =>
            {
                Logger.Info("Python server error: " + msg);
                OnServerError?.Invoke(msg);
            }));
        }

        ~Server()
        {
            Stop();
        }

        /// <summary>Injection constructor for unit tests: accepts a pre-wired transport and dispatcher.</summary>
        /// <param name="transport">Stub transport (e.g. NullTransport) for testing.</param>
        /// <param name="dispatcher">Stub dispatcher for testing.</param>
        protected Server(ITransportServer transport, IMainThreadDispatcher dispatcher)
        {
            Logger = CreateDefaultLogger();
            _dispatcher = dispatcher;
            _transport = transport;
            var bus = BuildCoreStack(out JsonSerializerOptions jsonOptions);
            var protocol = new JsonRpcProtocol(bus, bytes => transport.SendAll(new ArraySegment<byte>(bytes)), () => Logger, jsonOptions, ex => OnError?.Invoke(ex));
            transport.OnData += (id, seg) => protocol.HandleMessage(seg.Array);
        }

        /// <summary>
        /// Build the shared RPC core (serializer options, bus, registrar, registries, converters)
        /// used by both constructors. Transport wiring is left to each constructor.
        /// </summary>
        /// <param name="jsonOptions">Shared JSON serializer options, also needed by the transport.</param>
        /// <returns>The configured <see cref="RpcBus"/>.</returns>
        protected RpcBus BuildCoreStack(out JsonSerializerOptions jsonOptions)
        {
            jsonOptions = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            var fallbackJsonOptions = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
            var preJsonOptions = new JsonSerializerOptions
            {
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                TypeInfoResolver = new ConverterOnlyResolver(),
            };
            var chain = new JsonSerializerChain(preJsonOptions, jsonOptions, fallbackJsonOptions);
            var bus = new RpcBus(() => Logger, chain);
            _registry = new ObjectRegistry(() => Logger);
            _typeResolver = new TypeResolver(() => Logger);
            Rpc = new RpcRegistrar(bus, chain, () => Logger, _typeResolver);
            AddConverters();
            AddFallbackConverters();
            return bus;
        }

        // ─── Lifecycle ───────────────────────────────────────────────────────────

        /// <summary>Stop the server and disconnect all clients.</summary>
        public void Stop()
        {
            if (!IsRunning)
            {
                return;
            }

            if (_stdioTransport != null)
            {
                _stdioTransport.Stop();
                try
                {
                    if (_pythonProcess != null)
                    {
                        // conda run spawns a process tree (conda -> cmd -> python).
                        // Kill the entire tree so the Python server is also terminated.
                        var tk = Process.Start(new ProcessStartInfo("taskkill", "/F /T /PID " + _pythonProcess.Id)
                        {
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                        });
                        tk?.WaitForExit(3000);
                        _pythonProcess.Kill();
                    }
                }
                catch { }
                _pythonProcess = null;
            }
            else
            {
                _transport.Stop();
            }
            _dispatcher.Stop();
            _connectedClients.Clear();
            IsRunning = false;
            Logger.Info("Server stopped");
        }

        /// <summary>True while the server is actively listening for connections.</summary>
        public bool IsRunning { get; protected set; }

        // ─── Event push to Python ────────────────────────────────────────────────

        /// <summary>Push a named event with optional payload to all connected Python clients.</summary>
        /// <param name="eventType">Event name string.</param>
        /// <param name="data">Optional key-value payload dictionary.</param>
        public void SendEvent(string eventType, Dictionary<string, object> data = null)
        {
            if (!IsRunning || _connectedClients.Count == 0 || _transport == null)
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
