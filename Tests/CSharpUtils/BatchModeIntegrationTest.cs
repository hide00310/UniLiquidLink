using System;
using UnityEditor;
using UnityEngine;

namespace UniLiquidLink
{
    /// <summary>
    /// Command-line entry point for starting the Integration Test Server without the Editor UI.
    /// Invoke via: -executeMethod UniLiquidLink.BatchModeIntegrationTest.StartServer
    /// Must be launched WITHOUT -quit; the Editor process stays alive to host the server.
    /// </summary>
    public static class BatchModeIntegrationTest
    {
        const string CommandEnvVar = "LLIQUIDLINK_PYTHON_SERVER_COMMAND";

        public static void StartServer()
        {
            string command = Environment.GetEnvironmentVariable(CommandEnvVar);
            if (string.IsNullOrEmpty(command))
            {
                Debug.LogError("[BatchModeIntegrationTest] " + CommandEnvVar + " is not set.");
                EditorApplication.Exit(1);
                return;
            }

            EditorPrefs.SetString(UniLiquidLinkIntegrationTest.PrefsKeyCommand, command);
            UniLiquidLinkIntegrationTest.DebugStart();
            Debug.Log("[BatchModeIntegrationTest] READY");
        }
    }
}
