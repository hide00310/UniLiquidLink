using UnityEditor;
using UnityEditor.Compilation;

namespace UniLiquidLink
{
    // Companion to BatchModeSampleImport: re-subscribes to compilationFinished after
    // every domain reload (a plain event subscription made before a reload is wiped
    // out along with all other static state), and immediately calls
    // TryEmitReadyOrChain() once on load to cover the case where the reload
    // triggered by Sample.Import() already completed compilation before this
    // static constructor runs.
    [InitializeOnLoad]
    internal static class BatchModeSampleImportReload
    {
        static BatchModeSampleImportReload()
        {
            CompilationPipeline.compilationFinished += OnCompilationFinished;
            BatchModeSampleImport.TryEmitReadyOrChain();
        }

        static void OnCompilationFinished(object obj)
        {
            BatchModeSampleImport.TryEmitReadyOrChain();
        }
    }
}
