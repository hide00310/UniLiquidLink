using UnityEditor;
using UnityEditor.PackageManager.UI;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace UniLiquidLink
{
    // Command-line entry point for importing the UniLiquidLink Samples (via Package
    // Manager) into a target project that has never imported them, then chaining
    // into SampleServerTest.BatchStartCubeDemo() once compilation settles.
    // Invoke via: -executeMethod UniLiquidLink.BatchModeSampleImport.ImportSamplesThenStartCubeDemo
    // Must be launched WITHOUT -quit; the Editor process stays alive to host the server.
    //
    // Importing the sample may add new .cs files under Assets/Samples/, which can
    // trigger a domain reload. Reload discards all static state, so this class
    // cannot simply await a callback registered here -- the [InitializeOnLoad]
    // companion BatchModeSampleImportReload re-subscribes after every reload and
    // also calls TryEmitReadyOrChain(), covering the case where reload already
    // happened by the time this method's own wait loop would have fired.
    public static class BatchModeSampleImport
    {
        const string SampleDisplayName = "UniLiquidLink Samples";
        const string FallbackPackageName = "com.hide00310.uniliquidlink";

        // Persists across domain reload (unlike static fields), so the ready-chain
        // logic in TryEmitReadyOrChain knows whether an import is in flight.
        const string SessionKeyWaitingForReady = "UniLiquidLink.BatchModeSampleImport.WaitingForReady";

        // Number of EditorApplication.update ticks to observe isCompiling before
        // concluding that Import() did not trigger a compile. Import() returning
        // does not guarantee isCompiling has already flipped true on the same frame.
        const int NoCompileConfirmFrames = 30;

        public static void ImportSamplesThenStartCubeDemo()
        {
            string packageName;
            string packageVersion;
            if (!ResolvePackageInfo(out packageName, out packageVersion))
            {
                return; // ResolvePackageInfo already logged and called EditorApplication.Exit(1).
            }

            Sample? sample = FindSample(packageName, packageVersion);
            if (sample == null)
            {
                Debug.LogError(string.Format(
                    "[BatchModeSampleImport] Sample \"{0}\" not found for package {1}@{2}.",
                    SampleDisplayName, packageName, packageVersion));
                EditorApplication.Exit(1);
                return;
            }

            SessionState.SetBool(SessionKeyWaitingForReady, true);

            if (!sample.Value.isImported)
            {
                sample.Value.Import();
            }

            BeginObserveCompile();
        }

        static bool ResolvePackageInfo(out string packageName, out string packageVersion)
        {
            PackageInfo info = PackageInfo.FindForAssembly(typeof(BatchModeSampleImport).Assembly);
            if (info == null)
            {
                Debug.LogWarning(string.Format(
                    "[BatchModeSampleImport] PackageInfo.FindForAssembly could not resolve this " +
                    "assembly's package; falling back to package name \"{0}\".", FallbackPackageName));

                foreach (PackageInfo p in PackageInfo.GetAllRegisteredPackages())
                {
                    if (p.name == FallbackPackageName)
                    {
                        info = p;
                        break;
                    }
                }
            }

            if (info == null)
            {
                Debug.LogError(string.Format(
                    "[BatchModeSampleImport] Could not resolve package \"{0}\" via Package Manager.",
                    FallbackPackageName));
                EditorApplication.Exit(1);
                packageName = null;
                packageVersion = null;
                return false;
            }

            packageName = info.name;
            packageVersion = info.version;
            return true;
        }

        static Sample? FindSample(string packageName, string packageVersion)
        {
            foreach (Sample s in Sample.FindByPackage(packageName, packageVersion))
            {
                if (s.displayName == SampleDisplayName)
                {
                    return s;
                }
            }
            return null;
        }

        static void BeginObserveCompile()
        {
            _framesObserved = 0;
            EditorApplication.update += ObserveCompileTick;
        }

        static int _framesObserved;

        static void ObserveCompileTick()
        {
            _framesObserved++;

            if (EditorApplication.isCompiling)
            {
                EditorApplication.update -= ObserveCompileTick;
                return; // BatchModeSampleImportReload will fire once reload+compile finish.
            }

            if (_framesObserved >= NoCompileConfirmFrames)
            {
                EditorApplication.update -= ObserveCompileTick;
                TryEmitReadyOrChain();
            }
        }

        // Idempotent: safe to call from multiple entry points (see class comment).
        // Only proceeds once, past any in-progress compile, and only if an import
        // was actually started by ImportSamplesThenStartCubeDemo in this session.
        internal static void TryEmitReadyOrChain()
        {
            if (EditorApplication.isCompiling)
            {
                return;
            }

            if (!SessionState.GetBool(SessionKeyWaitingForReady, false))
            {
                return;
            }

            SessionState.SetBool(SessionKeyWaitingForReady, false);
            Debug.Log("[BatchModeSampleImport] READY");
            SampleServerTest.BatchStartCubeDemo();
        }
    }
}
