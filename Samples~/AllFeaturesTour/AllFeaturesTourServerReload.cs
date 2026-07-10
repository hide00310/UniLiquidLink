using UnityEditor;

namespace UniLiquidLink.Samples
{
    [InitializeOnLoad]
    internal static class AllFeaturesTourServerReload
    {
        const string SessionKeyWasRunning = "UniLiquidLink.AllFeaturesTourServer.WasRunning";

        static AllFeaturesTourServerReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
        }

        static void OnBeforeReload()
        {
            if (AllFeaturesTourServer.IsRunning)
            {
                SessionState.SetBool(SessionKeyWasRunning, true);
                AllFeaturesTourServer.StopServer();
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
                string pythonServerStartCommand = EditorPrefs.GetString(AllFeaturesTourServer.PrefsKeyCommand, "");
                AllFeaturesTourServer.StartServer(pythonServerStartCommand);
            }
        }
    }
}
