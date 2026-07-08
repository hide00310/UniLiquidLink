using UnityEditor;

namespace UniLiquidLink
{
    [InitializeOnLoad]
    internal static class IntegrationTestServerReload
    {
        const string SessionKeyWasRunning = "UniLiquidLink.IntegrationTest.WasRunning";

        static IntegrationTestServerReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterReload;
        }

        static void OnBeforeReload()
        {
            if (UniLiquidLinkIntegrationTest.IsRunning)
            {
                SessionState.SetBool(SessionKeyWasRunning, true);
                UniLiquidLinkIntegrationTest.DebugStop();
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
                UniLiquidLinkIntegrationTest.DebugStart();
            }
        }
    }
}
