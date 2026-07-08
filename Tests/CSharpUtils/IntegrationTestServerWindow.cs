using UnityEditor;
using UnityEngine;

namespace UniLiquidLink
{
    internal class IntegrationTestServerWindow : EditorWindow
    {
        string _pythonServerStartCommand;

        [MenuItem("UniLiquidLink/Tests/Integration Test Server Window")]
        public static void Open()
        {
            GetWindow<IntegrationTestServerWindow>("Integration Test Server");
        }

        void OnEnable()
        {
            _pythonServerStartCommand = EditorPrefs.GetString(UniLiquidLinkIntegrationTest.PrefsKeyCommand, "");
            EditorApplication.update += Repaint;
        }

        void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        void OnGUI()
        {
            // ProcessStartInfo.FileName does not resolve relative to WorkingDirectory, so an absolute path is required.
            EditorGUILayout.LabelField("Python Server Start Command (absolute path required)");
            string newCommand = EditorGUILayout.TextField(_pythonServerStartCommand);
            if (newCommand != _pythonServerStartCommand)
            {
                _pythonServerStartCommand = newCommand;
                EditorPrefs.SetString(UniLiquidLinkIntegrationTest.PrefsKeyCommand, _pythonServerStartCommand);
            }

            bool running = UniLiquidLinkIntegrationTest.IsRunning;
            if (GUILayout.Toggle(running, running ? "Stop" : "Start", "Button") != running)
            {
                if (running)
                {
                    UniLiquidLinkIntegrationTest.DebugStop();
                }
                else
                {
                    UniLiquidLinkIntegrationTest.DebugStart();
                }
            }
        }
    }
}
