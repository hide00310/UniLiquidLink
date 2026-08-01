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
    }
}
