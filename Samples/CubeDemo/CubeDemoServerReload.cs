using UnityEditor;

namespace UniLiquidLink.Samples
{
    [InitializeOnLoad]
    internal static class CubeDemoServerReload
    {
        const string SessionKeyWasRunning = "UniLiquidLink.CubeDemoServer.WasRunning";

        static CubeDemoServerReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
        }

        static void OnBeforeReload()
        {
            if (CubeDemoServer.IsRunning)
            {
                SessionState.SetBool(SessionKeyWasRunning, true);
                CubeDemoServer.StopServer();
            }
            else
            {
                SessionState.SetBool(SessionKeyWasRunning, false);
            }
        }

        static void OnAfterReload()
        {
            if (SessionState.GetBool(SessionKeyWasRunning, false))
            {
                SessionState.SetBool(SessionKeyWasRunning, false);
                string pythonServerStartCommand = EditorPrefs.GetString(CubeDemoServer.PrefsKeyCommand, "");
                CubeDemoServer.StartServer(pythonServerStartCommand);
            }
        }
    }
}
