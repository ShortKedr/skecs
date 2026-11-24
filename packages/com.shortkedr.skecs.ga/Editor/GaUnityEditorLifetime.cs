using UnityEditor;

namespace SKECS.GA.Unity.Editor
{
    [InitializeOnLoad]
    internal static class GaUnityEditorLifetime
    {
        static GaUnityEditorLifetime()
        {
            AssemblyReloadEvents.beforeAssemblyReload += GaUnityPackedKernel.Shutdown;
            EditorApplication.quitting += GaUnityPackedKernel.Shutdown;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.ExitingPlayMode)
                    GaUnityPackedKernel.Shutdown();
            };
        }
    }
}
