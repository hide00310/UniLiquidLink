using System;
using System.IO;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;

namespace UniLiquidLink.Samples
{
    internal class UniLiquidLinkSampleWindow : EditorWindow
    {
        enum SampleKind
        {
            CubeDemo,
            AllFeaturesTour
        }

        static readonly string[] SampleLabels = { "Cube Demo", "All Features Tour" };

        SampleKind _selectedSample;
        string _pythonCommand;

        [MenuItem("UniLiquidLink/Samples/Sample Window")]
        public static void Open()
        {
            GetWindow<UniLiquidLinkSampleWindow>("UniLiquidLink Samples");
        }

        void OnEnable()
        {
            // Not persisted on purpose: every reopen starts from the same defaults.
            _selectedSample = SampleKind.CubeDemo;
            _pythonCommand = "python";
            EditorApplication.update += Repaint;
        }

        void OnDisable()
        {
            EditorApplication.update -= Repaint;
        }

        // Same [CallerFilePath] pattern as UniLiquidLinkServer.cs, so the script path
        // resolves correctly regardless of where the package/sample is checked out.
        static string GetSampleRootDirectory([CallerFilePath] string sourceFilePath = "")
        {
            return Path.GetDirectoryName(sourceFilePath);
        }

        static string GetMiddlewareScriptPath()
        {
            return Path.Combine(GetSampleRootDirectory(), "run_middleware_server.py");
        }

        void OnGUI()
        {
            bool cubeDemoRunning = CubeDemoServer.IsRunning;
            bool allFeaturesTourRunning = AllFeaturesTourServer.IsRunning;
            bool anyRunning = cubeDemoRunning || allFeaturesTourRunning;

            EditorGUILayout.LabelField("Sample", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(anyRunning))
            {
                _selectedSample = (SampleKind)GUILayout.Toolbar((int)_selectedSample, SampleLabels);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Python Command", EditorStyles.boldLabel);
            _pythonCommand = EditorGUILayout.TextField(_pythonCommand);

            string scriptPath = GetMiddlewareScriptPath();
            EditorGUILayout.LabelField("Resolved Script Path (read-only)");
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(scriptPath);
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Cube Demo and All Features Tour both bind to port 8700, so only one can run "
                + "at a time. Stop the running sample before starting the other.",
                MessageType.Info);

            string fullCommand = string.Format("{0} \"{1}\"", _pythonCommand, scriptPath);

            EditorGUILayout.Space();
            if (_selectedSample == SampleKind.CubeDemo)
            {
                DrawServerControls(
                    "Cube Demo", cubeDemoRunning, allFeaturesTourRunning, fullCommand,
                    CubeDemoServer.StartServer, CubeDemoServer.StopServer);
            }
            else
            {
                DrawServerControls(
                    "All Features Tour", allFeaturesTourRunning, cubeDemoRunning, fullCommand,
                    AllFeaturesTourServer.StartServer, AllFeaturesTourServer.StopServer);
            }
        }

        static void DrawServerControls(
            string label, bool running, bool otherRunning, string fullCommand,
            Action<string> start, Action stop)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            using (new EditorGUI.DisabledScope(otherRunning))
            {
                if (GUILayout.Toggle(running, running ? "Stop" : "Start", "Button") != running)
                {
                    if (running)
                    {
                        stop();
                    }
                    else
                    {
                        start(fullCommand);
                    }
                }
            }
        }
    }
}
