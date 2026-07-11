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
            RpcBus.AdditionalDefaultValueAttributeTypes.Add(typeof(UnityEngine.Internal.DefaultValueAttribute));
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

        /// <summary>Start the server, register transport event handlers, and begin processing connections.</summary>
        public void Start()
        {
            if (_stdioTransport != null)
            {
                string workDir = WorkingDirectory
                    ?? GetRootLibDirectory();
                Rpc.SaveRpcNamesCsv(System.IO.Path.Combine(workDir, "rpc_names.csv"));
                _typeResolver.SaveAllowedTypesCsv(System.IO.Path.Combine(workDir, "type_names.csv"));
                _dispatcher.Start();
                _pythonProcess = StartPythonMiddleware(_pythonServerStartCommand);
                _stdioTransport.Start(
                    _pythonProcess.StandardOutput.BaseStream,
                    _pythonProcess.StandardInput.BaseStream,
                    _pythonProcess.StandardError.BaseStream
                );
                IsRunning = true;
                Logger.Info("Server started (stdio mode)");
                return;
            }

            _transport.OnConnect += (id, ep) => { _connectedClients.Add(id); Logger.Info($"Python connected (id={id})"); };
            _transport.OnDisconnect += id =>
            {
                _connectedClients.Remove(id);
                Logger.Info($"Python disconnected (id={id})");
                RaiseOnDisconnect(id);
            };
            _transport.OnError += (id, ex) =>
            {
                Logger.Info($"Error (id={id}): {ex?.Message}");
                OnError?.Invoke(ex);
            };
            _dispatcher.Start();
            _transport.Start();
            IsRunning = true;
            Logger.Info($"Server started");
        }
        private static string GetCurrentDirectory([CallerFilePath] string path = null)
        {
            return string.IsNullOrEmpty(path) ? null : Path.GetDirectoryName(path);
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

        Process StartPythonMiddleware(string pythonServerStartCommand)
        {
            string workDir = WorkingDirectory
                ?? GetRootLibDirectory();
            string[] cmds = pythonServerStartCommand.Split(" ");
            string fileName = cmds[0];
            string arguments = string.Join(" ", cmds, 1, cmds.Length - 1);
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                WorkingDirectory = workDir
            };
            var p = Process.Start(psi);
            Logger.Info($"Python middleware started (pid={p.Id}, cmd={fileName} {arguments})");
            return p;
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
    }
}
