using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace UniLiquidLink
{
    // Menu-item driven start/stop for the Cube Demo / All Features Tour sample
    // servers, so pytest (test_sample_cube_demo.py / test_sample_all_features_tour.py)
    // has a live server to connect to, mirroring UniLiquidLinkIntegrationTest's
    // Start/Stop menu items for the Integration Test Server.
    //
    // The UniLiquidLink.Samples assembly only exists once the user imports the
    // Samples via Package Manager (they live under the UPM-ignored Samples~
    // folder until then), so all access to its types goes through reflection
    // instead of a direct assembly reference. This keeps UniLiquidLink.Tests
    // compiling in projects that haven't imported the samples.
    public static class SampleServerTest
    {
        const string SamplesAssemblyName = "UniLiquidLink.Samples";

        // Raw python.exe path (no quoting, no trailing arguments) for the batch-mode
        // entry points below. Distinct in shape from LLIQUIDLINK_PYTHON_SERVER_COMMAND
        // (BatchModeIntegrationTest.cs), which is already a complete
        // "<python.exe>" -m lliquidlink.server command string: BuildCommand() appends
        // the middleware script path itself, so feeding it that pre-built command
        // would duplicate/corrupt the resulting command line.
        const string PythonExeEnvVar = "LLIQUIDLINK_PYTHON_EXE";

        // Resolves run_middleware_server.py relative to this source file, so the
        // path is correct regardless of where the package/repo is checked out.
        static string GetMiddlewareScriptPath([CallerFilePath] string sourceFilePath = "")
        {
            return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFilePath),
                "../../Samples~/UniLiquidLinkSample/run_middleware_server.py"));
        }

        // Builds the child-process command line, reusing the run_python.bat path
        // configured via Integration Test Server Window (shared EditorPrefs key).
        static string BuildCommand()
        {
            string pythonBat = EditorPrefs.GetString(UniLiquidLinkIntegrationTest.PrefsKeyCommand, "");
            if (string.IsNullOrEmpty(pythonBat))
            {
                throw new InvalidOperationException(
                    "Python server start command is not configured. Open " +
                    "UniLiquidLink/Tests/Integration Test Server Window and set it, " +
                    "or set EditorPrefs key \"" + UniLiquidLinkIntegrationTest.PrefsKeyCommand + "\" directly.");
            }
            return string.Format("{0} \"{1}\"", pythonBat, GetMiddlewareScriptPath());
        }

        // True once the Samples package has been imported via Package Manager
        // (i.e. the UniLiquidLink.Samples assembly is loaded and its types resolve).
        static bool IsSamplesAvailable()
        {
            return Type.GetType(string.Format("UniLiquidLink.Samples.CubeDemoServer, {0}", SamplesAssemblyName)) != null;
        }

        // Resolves a static method on a UniLiquidLink.Samples type by name and invokes it.
        // Logs an error and returns early instead of throwing if the Samples assembly
        // isn't available, since the corresponding menu item may still be invoked
        // (e.g. via a hotkey) even while grayed out.
        static void InvokeSampleServerMethod(string typeName, string methodName, params object[] args)
        {
            Type type = Type.GetType(string.Format("UniLiquidLink.Samples.{0}, {1}", typeName, SamplesAssemblyName));
            if (type == null)
            {
                Debug.LogError(string.Format(
                    "[SampleServerTest] UniLiquidLink.Samples.{0} not found. " +
                    "Import UniLiquidLink Samples from Package Manager first.", typeName));
                return;
            }

            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            method.Invoke(null, args);
        }

        [MenuItem("UniLiquidLink/Tests/Sample Server Start (Cube Demo)")]
        public static void StartCubeDemo() { InvokeSampleServerMethod("CubeDemoServer", "StartServer", BuildCommand()); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Start (Cube Demo)", true)]
        public static bool ValidateStartCubeDemo() { return IsSamplesAvailable(); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Stop (Cube Demo)")]
        public static void StopCubeDemo() { InvokeSampleServerMethod("CubeDemoServer", "StopServer"); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Stop (Cube Demo)", true)]
        public static bool ValidateStopCubeDemo() { return IsSamplesAvailable(); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Start (All Features Tour)")]
        public static void StartAllFeaturesTour() { InvokeSampleServerMethod("AllFeaturesTourServer", "StartServer", BuildCommand()); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Start (All Features Tour)", true)]
        public static bool ValidateStartAllFeaturesTour() { return IsSamplesAvailable(); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Stop (All Features Tour)")]
        public static void StopAllFeaturesTour() { InvokeSampleServerMethod("AllFeaturesTourServer", "StopServer"); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Stop (All Features Tour)", true)]
        public static bool ValidateStopAllFeaturesTour() { return IsSamplesAvailable(); }

        // Reads PythonExeEnvVar and stores it under the shared PrefsKeyCommand so
        // BuildCommand() picks it up. Logs an error and exits the batch process on
        // failure instead of throwing, mirroring BatchModeIntegrationTest.StartServer.
        static bool TrySetCommandFromPythonExeEnvVar()
        {
            string pythonExe = Environment.GetEnvironmentVariable(PythonExeEnvVar);
            if (string.IsNullOrEmpty(pythonExe))
            {
                Debug.LogError("[SampleServerTest] " + PythonExeEnvVar + " is not set.");
                EditorApplication.Exit(1);
                return false;
            }

            EditorPrefs.SetString(UniLiquidLinkIntegrationTest.PrefsKeyCommand, pythonExe);
            return true;
        }

        // Command-line entry point for starting the Cube Demo sample server without
        // the Editor UI. Invoke via: -executeMethod UniLiquidLink.SampleServerTest.BatchStartCubeDemo
        // Must be launched WITHOUT -quit; the Editor process stays alive to host the server.
        public static void BatchStartCubeDemo()
        {
            // InvokeSampleServerMethod only logs an error and returns when the type is
            // missing, which would let this method fall through to logging READY for a
            // server that never started; guard explicitly instead.
            if (!IsSamplesAvailable())
            {
                Debug.LogError("[SampleServerTest] UniLiquidLink.Samples is not available; import UniLiquidLink Samples first.");
                EditorApplication.Exit(1);
                return;
            }

            if (!TrySetCommandFromPythonExeEnvVar())
            {
                return;
            }

            StartCubeDemo();
            Debug.Log("[SampleServerTest] CUBE_DEMO_READY");
        }

        // Command-line entry point for starting the All Features Tour sample server
        // without the Editor UI. Invoke via:
        // -executeMethod UniLiquidLink.SampleServerTest.BatchStartAllFeaturesTour
        // Must be launched WITHOUT -quit; the Editor process stays alive to host the server.
        public static void BatchStartAllFeaturesTour()
        {
            if (!IsSamplesAvailable())
            {
                Debug.LogError("[SampleServerTest] UniLiquidLink.Samples is not available; import UniLiquidLink Samples first.");
                EditorApplication.Exit(1);
                return;
            }

            if (!TrySetCommandFromPythonExeEnvVar())
            {
                return;
            }

            StartAllFeaturesTour();
            Debug.Log("[SampleServerTest] ALL_FEATURES_TOUR_READY");
        }
    }
}
