using UnityEditor;
using UnityEngine;

namespace UniLiquidLink.Samples
{
    internal class AllFeaturesTourServerWindow : EditorWindow
    {
        string _pythonServerStartCommand;

        [MenuItem("UniLiquidLink/Samples/All Features Tour Server Window")]
        public static void Open()
        {
            GetWindow<AllFeaturesTourServerWindow>("All Features Tour Server");
        }

        void OnEnable()
        {
            _pythonServerStartCommand = EditorPrefs.GetString(AllFeaturesTourServer.PrefsKeyCommand, "");
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
                EditorPrefs.SetString(AllFeaturesTourServer.PrefsKeyCommand, _pythonServerStartCommand);
            }

            bool running = AllFeaturesTourServer.IsRunning;
            if (GUILayout.Toggle(running, running ? "Stop" : "Start", "Button") != running)
            {
                if (running)
                {
                    AllFeaturesTourServer.StopServer();
                }
                else
                {
                    AllFeaturesTourServer.StartServer(_pythonServerStartCommand);
                }
            }
        }
    }
}
