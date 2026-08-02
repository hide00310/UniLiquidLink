using LLiquidLink.Logger;
using System;
using System.Diagnostics;

namespace LLiquidLink
{
    /// <summary>Owns the lifecycle (start/kill) of the Python middleware process. Unity-independent.</summary>
    public class PythonProcessManager
    {
        readonly Func<ILogger> _getLogger;
        readonly ProcessStartInfo _startInfo;

        /// <summary>Initialize the manager and assemble the process start info.</summary>
        /// <param name="getLogger">Factory that returns the current logger.</param>
        /// <param name="pythonServerStartCommand">Command line, e.g. "conda run -n base python server.py".</param>
        /// <param name="workingDirectory">Working directory for the process.</param>
        /// <param name="dataDir">Value passed to the process via the "-dataDir" argument.</param>
        public PythonProcessManager(Func<ILogger> getLogger, string pythonServerStartCommand, string workingDirectory, string dataDir)
        {
            _getLogger = getLogger;
            string[] cmds = pythonServerStartCommand.Split(' ');
            string fileName = cmds[0];
            string arguments = string.Join(" ", cmds, 1, cmds.Length - 1);
            arguments += " -dataDir " + dataDir;
            _startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = false,
                WorkingDirectory = workingDirectory
            };
        }

        /// <summary>The most recently started Python process, or <c>null</c> if not started / already killed.</summary>
        public Process Process { get; private set; }

        /// <summary>Start the Python middleware process using the start info assembled in the constructor.</summary>
        /// <returns>The started process.</returns>
        public Process Start()
        {
            Process = System.Diagnostics.Process.Start(_startInfo);
            _getLogger().Info("Python middleware started (pid=" + Process.Id + ", cmd=" + _startInfo.FileName + " " + _startInfo.Arguments + ")");
            return Process;
        }

        /// <summary>Kill the Python process tree, if running. Swallows errors (best-effort cleanup).</summary>
        public void Kill()
        {
            try
            {
                if (Process != null)
                {
                    // conda run spawns a process tree (conda -> cmd -> python).
                    // Kill the entire tree so the Python server is also terminated.
                    // Output is not read, so it must not be redirected: a redirected pipe that
                    // fills without a reader would block taskkill from exiting.
                    using (var tk = System.Diagnostics.Process.Start(new ProcessStartInfo("taskkill", "/F /T /PID " + Process.Id)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    }))
                    {
                        if (tk != null) tk.WaitForExit(3000);
                    }
                    // taskkill should have already terminated the process; only fall back to
                    // Process.Kill() if it is somehow still alive (Kill() throws on an exited process).
                    if (!Process.HasExited)
                    {
                        Process.Kill();
                    }
                    Process.Dispose();
                }
            }
            catch { }
            Process = null;
        }
    }
}
