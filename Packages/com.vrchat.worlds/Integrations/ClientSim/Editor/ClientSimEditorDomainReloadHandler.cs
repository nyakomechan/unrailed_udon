using UnityEditor;
using UnityEngine;

namespace VRC.SDK3.ClientSim.Editor
{
    internal static class ClientSimEditorDomainReloadHandler
    {
        [InitializeOnLoadMethod]
        public static void Initialize()
        {
            if (Application.isPlaying)
            {
                // Reloaded while in play mode. Awake() won't run. Try to recover.
                ClientSimRuntimeLoader.StartClientSim();
            }

            AssemblyReloadEvents.beforeAssemblyReload -= HandleAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += HandleAssemblyReload;
        }

        private static void HandleAssemblyReload()
        {
            ClientSimRuntimeLoader.StopClientSim();
        }
    }
}