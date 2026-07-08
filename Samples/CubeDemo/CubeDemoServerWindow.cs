using UnityEditor;
using UnityEngine;

namespace UniLiquidLink.Samples
{
    internal class CubeDemoServerWindow : EditorWindow
    {
        string _pythonServerStartCommand;

        [MenuItem("UniLiquidLink/Samples/Cube Demo Server Window")]
        public static void Open()
        {
            GetWindow<CubeDemoServerWindow>("Cube Demo Server");
        }

        void OnEnable()
        {
            _pythonServerStartCommand = EditorPrefs.GetString(CubeDemoServer.PrefsKeyCommand, "");
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
                EditorPrefs.SetString(CubeDemoServer.PrefsKeyCommand, _pythonServerStartCommand);
            }

            bool running = CubeDemoServer.IsRunning;
            if (GUILayout.Toggle(running, running ? "Stop" : "Start", "Button") != running)
            {
                if (running)
                {
                    CubeDemoServer.StopServer();
                }
                else
                {
                    CubeDemoServer.StartServer(_pythonServerStartCommand);
                }
            }
        }
    }
}
