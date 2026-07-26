using LLiquidLink;
using LLiquidLink.Logger;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace UniLiquidLink
{
    /// <summary>
    /// Unity-specific entry point for the WebSocket-RPC stack; adds Python-middleware process
    /// management, Unity object registration, and Unity-flavored logging on top of the
    /// Unity-independent <see cref="LLiquidLink.Server"/> base class.
    /// </summary>
    public partial class Server : LLiquidLink.Server
    {
        /// <summary>Default logger that forwards all messages to <c>UnityEngine.Debug.LogError</c>.</summary>
        class DefaultLogger : ILogger
        {
            /// <inheritdoc/>
            public LogLevel MinLevel { get; set; }

            /// <summary>Initialize with <see cref="LogLevel.Info"/> as the default minimum level.</summary>
            public DefaultLogger()
            {
                MinLevel = LogLevel.Info;
            }

            /// <inheritdoc/>
            public void Info(string msg)
            {
                if (MinLevel <= LogLevel.Info)
                {
                    UnityEngine.Debug.LogError("[UniLiquidLink] " + msg);
                }
            }

            /// <inheritdoc/>
            public void Debug(string msg)
            {
                if (MinLevel <= LogLevel.Debug)
                {
                    UnityEngine.Debug.LogError("[UniLiquidLink] " + msg);
                }
            }

            /// <inheritdoc/>
            public void InfoFormat(string format, params object[] args)
            {
                if (MinLevel <= LogLevel.Info)
                {
                    UnityEngine.Debug.LogErrorFormat("[UniLiquidLink] " + format, args);
                }
            }

            /// <inheritdoc/>
            public void DebugFormat(string format, params object[] args)
            {
                if (MinLevel <= LogLevel.Debug)
                {
                    UnityEngine.Debug.LogErrorFormat("[UniLiquidLink] " + format, args);
                }
            }
        }
        string _pythonServerStartCommand;

        /// <inheritdoc/>
        protected override ILogger CreateDefaultLogger()
        {
            return new DefaultLogger();
        }

        static Server()
        {
            RpcRegistry.AdditionalDefaultValueAttributeTypes.Add(typeof(UnityEngine.Internal.DefaultValueAttribute));
        }

        /// <summary>Production constructor; starts Python middleware and communicates via stdio.</summary>
        public Server(string pythonServerStartCommand) : base(new MainThreadDispatcher())
        {
            _pythonServerStartCommand = pythonServerStartCommand;
        }

        /// <summary>Injection constructor for unit tests: accepts a pre-wired transport and dispatcher.</summary>
        /// <param name="transport">Stub transport (e.g. NullTransport) for testing.</param>
        /// <param name="dispatcher">Stub dispatcher for testing.</param>
        public Server(string pythonServerStartCommand, ITransportServer transport, IMainThreadDispatcher dispatcher) : base(transport, dispatcher)
        {
            _pythonServerStartCommand = pythonServerStartCommand;
        }

        // ─── Lifecycle ───────────────────────────────────────────────────────────

        /// <summary>Start the server, launching the Python middleware first when running in stdio mode.</summary>
        public void Start()
        {
            if (IsRunning)
            {
                return;
            }
            if (_transport is StdioTransport)
            {
                string workDir = WorkingDirectory ?? GetRootLibDirectory();
                Rpc.SaveRpcNamesCsv(Path.Combine(ServerDir, "Data/rpc_names.csv"));
                _typeResolver.SaveAllowedTypesCsv(Path.Combine(ServerDir, "Data/type_names.csv"));
                _pythonProcessManager = new PythonProcessManager(() => Logger, _pythonServerStartCommand, workDir, ServerDir + "/Data");
                Process p = _pythonProcessManager.Start();
                ((StdioTransport)_transport).AttachStreams(p.StandardOutput.BaseStream, p.StandardInput.BaseStream, p.StandardError.BaseStream);
            }
            _dispatcher.Start();
            _transport.Start();
            IsRunning = true;
            Logger.Info("Server started" + (_pythonProcessManager != null ? " (stdio mode)" : ""));
        }

        // Resolve the WebSocketLib root directory next to this source file.
        private static string GetRootLibDirectory([CallerFilePath] string path = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string csharpUnityDir = Path.GetDirectoryName(path);
            return Path.GetFullPath(Path.Combine(csharpUnityDir, "..", "..", ".."));
        }

        // ─── Delegating methods ───────────────────────────────────────────────────

        /// <summary>Register <paramref name="obj"/> in the object registry so it can be referenced by instance ID.</summary>
        /// <param name="obj">Unity object to register.</param>
        public void RegisterObject(UnityEngine.Object obj)
        {
            _registry.RegisterObject(obj);
        }

        /// <summary>Remove <paramref name="obj"/> from the object registry.</summary>
        /// <param name="obj">Unity object to unregister.</param>
        public void UnregisterObject(UnityEngine.Object obj)
        {
            _registry.UnregisterObject(obj);
        }

        private void AddUniConverters()
        {
            Rpc.AddConverterAndRegister(_jsonChain.Options[(int)JsonSerializerChain.Stage.Main], new UnityObjectConverter(_registry));
            Rpc.AddConverter(_jsonChain.Options[(int)JsonSerializerChain.Stage.Fallback], new JsonUtilityConverterFactory());
        }

        protected override void Initialize()
        {
            base.Initialize();
            AddUniConverters();
        }
    }
}
