using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UniLiquidLink.Samples;

namespace UniLiquidLink
{
    // Menu-item driven start/stop for the Cube Demo / All Features Tour sample
    // servers, so pytest (test_sample_cube_demo.py / test_sample_all_features_tour.py)
    // has a live server to connect to, mirroring UniLiquidLinkIntegrationTest's
    // Start/Stop menu items for the Integration Test Server.
    public static class SampleServerTest
    {
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

        [MenuItem("UniLiquidLink/Tests/Sample Server Start (Cube Demo)")]
        public static void StartCubeDemo() { CubeDemoServer.StartServer(BuildCommand()); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Stop (Cube Demo)")]
        public static void StopCubeDemo() { CubeDemoServer.StopServer(); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Start (All Features Tour)")]
        public static void StartAllFeaturesTour() { AllFeaturesTourServer.StartServer(BuildCommand()); }

        [MenuItem("UniLiquidLink/Tests/Sample Server Stop (All Features Tour)")]
        public static void StopAllFeaturesTour() { AllFeaturesTourServer.StopServer(); }
    }
}
