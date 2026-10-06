using UnityEditor;
using UnityEngine;
using VRC.Dynamics;
using VRC.SDK3.Dynamics.PhysBone;

namespace VRC.SDK3.Dynamics
{
    /// <summary>
    /// Project agnostic VRC dynamics setup.
    /// </summary>
    public static class DynamicsSetup
    {
        internal static void EditorInit()
        {
            VRCConstraintManager.CanExecuteConstraintJobsInEditMode = VRC.SDKBase.Editor.VRCSettings.VrcConstraintsInEditMode;
            EditorApplication.playModeStateChanged -= HandlePlayModeStateChanged;
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;

            AssemblyReloadEvents.beforeAssemblyReload -= HandlePreAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += HandlePreAssemblyReload;
        }

        private static void HandlePlayModeStateChanged(PlayModeStateChange stateChange)
        {
            switch (stateChange)
            {
                case PlayModeStateChange.EnteredPlayMode:
                case PlayModeStateChange.ExitingPlayMode:
                    VRCDynamicsScheduler.HandleEditorPlayModeToggle();
                    break;
            }
        }

        private static void HandlePreAssemblyReload()
        {
            // Don't leak

            if (ContactManager.Inst != null)
            {
                ContactManager.Inst.Dispose();
                ContactManager.Inst = null;
            }
            foreach (ContactBase contact in Object.FindObjectsByType<ContactBase>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                contact.ClearInit();
            }

            if (PhysBoneManager.Inst != null)
            {
                PhysBoneManager.Inst.Dispose();
                PhysBoneManager.Inst = null;
            }
        }

        internal static void RuntimeInit(bool invokedFromDomainReload)
        {
            SetupDynamicsManagers(invokedFromDomainReload);
        }

        private static void SetupDynamicsManagers(bool invokedFromDomainReload)
        {
            //Create or recover singleton MonoBehaviours needed by dynamics

            //Contact Manager
            if (ContactManager.Inst == null)
            {
                ContactManager contactManager = Object.FindAnyObjectByType<ContactManager>();
                bool gotExistingContactManager = contactManager != null;
                if (contactManager == null)
                {
                    var obj = new GameObject("ContactManager");
                    Object.DontDestroyOnLoad(obj);
                    obj.hideFlags = HideFlags.HideInHierarchy;

                    contactManager = obj.AddComponent<ContactManager>();
                }

                ContactManager.Inst = contactManager;
                ContactManager.Inst.Init();

                if (invokedFromDomainReload && gotExistingContactManager)
                {
                    // Manually prompt all contacts to register themselves again because Start() does not run in response to domain reloads.
                    ContactBase[] contacts = Object.FindObjectsByType<ContactBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (ContactBase contact in contacts)
                    {
                        contact.Start();
                    }
                }
            }

            //PhysBone Manager
            if (PhysBoneManager.Inst == null)
            {
                PhysBoneManager physBoneManager = Object.FindAnyObjectByType<PhysBoneManager>();
                bool gotExistingPhysBoneManager = physBoneManager != null;
                if (physBoneManager == null)
                {
                    var obj = new GameObject("PhysBoneManager");
                    Object.DontDestroyOnLoad(obj);
                    obj.hideFlags = HideFlags.HideInHierarchy;

                    physBoneManager = obj.AddComponent<PhysBoneManager>();
                }

                PhysBoneManager.Inst = physBoneManager;
                PhysBoneManager.Inst.IsSDK = true;
                PhysBoneManager.Inst.Init();

                if (invokedFromDomainReload && gotExistingPhysBoneManager)
                {
                    // Manually prompt all contacts to register themselves again because Start() does not run in response to domain reloads.
                    VRCPhysBoneBase[] physBones = Object.FindObjectsByType<VRCPhysBoneBase>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    foreach (VRCPhysBoneBase physBone in physBones)
                    {
                        physBone.Init();
                    }
                }
            }

            //Constraint Manager is not a MonoBehaviour and serves itself
        }
    }
}