using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;

namespace VRC.SDK3.Editor
{
    public class VRCWorldPostBuildValidator : IProcessSceneWithReport
    {
        private static readonly List<GameObject> RootGameObjectsBuffer = new List<GameObject>();

        public int callbackOrder => int.MaxValue - 8;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // ensure we're not simply loading an AssetBundle
            if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            scene.GetRootGameObjects(RootGameObjectsBuffer);

            // Very stripped down validation. Defends against the user critically footgunning themselves through a scene process callback.
            // Only throw BuildFailedException. Any other exception won't stop the Unity build.

            List<PipelineManager> pipelineManagers = GetComponentsInScene<PipelineManager>();
            if (pipelineManagers.Count > 1)
            {
                throw new BuildFailedException("There are multiple pipeline manager components in the built scene. There must be exactly one. Your project might contain build scripts adding pipeline managers at build time.");
            }
            else if (pipelineManagers.Count == 0)
            {
                throw new BuildFailedException("The built scene has no pipeline manager component. There must be exactly one. Your project might contain build scripts removing pipeline managers at build time.");
            }
        }

        private List<T> GetComponentsInScene<T>()
        {
            List<T> results = new List<T>();

            foreach (GameObject go in RootGameObjectsBuffer)
            {
                results.AddRange(go.GetComponentsInChildren<T>(true));
            }

            return results;
        }
    }
}