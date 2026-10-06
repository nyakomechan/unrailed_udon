#if UNITY_EDITOR
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDKBase;

namespace MultiSim
{
    /// <summary>
    /// Tracks every networked scene object by its VRChat network id.
    /// ClientSim already assigns deterministic network ids (ordered by transform path) via
    /// ClientSimNetworkingView, and ships per-component encoders behind ClientSimNetworkIdHolder,
    /// but never attaches the holder to scene objects because it has no real transport.
    /// This registry attaches the holders so their Encode/Decode pipeline can be driven remotely.
    /// </summary>
    internal class MultiSimRegistry
    {
        private readonly Dictionary<int, ClientSimNetworkIdHolder> _holders =
            new Dictionary<int, ClientSimNetworkIdHolder>();

        // Every networked GameObject by id, including ones without syncable data
        // (e.g. a station whose GameObject has no synced variables).
        private readonly Dictionary<int, GameObject> _objects = new Dictionary<int, GameObject>();

        public IReadOnlyDictionary<int, ClientSimNetworkIdHolder> Holders => _holders;

        public void BuildFromScene()
        {
            _holders.Clear();
            _objects.Clear();

            VRC_SceneDescriptor descriptor = VRC_SceneDescriptor.Instance;
            if (descriptor == null)
            {
                MultiSimLog.Error("No scene descriptor; cannot register networked objects.");
                return;
            }

            // ClientSim runs this during its BeforeSceneLoad startup, which is too early to see
            // scene objects in editor play mode, so no ClientSimNetworkingViews get attached.
            // The call is idempotent (ids come from the descriptor's NetworkIDCollection).
            //
            // ClientSim caches its VRCPlayerObject template list on first use, before any
            // per-player runtime copies exist. Re-running ConfigureNetworkOnScene here would
            // use that stale list for its persistence-object exclusion and re-assign the
            // already-instantiated copies scene-style network ids. Force a fresh scan for
            // this call, then restore the template-only cache: SetupPlayerPersistence
            // instantiates every entry of that list per player, so runtime copies must
            // never end up in it.
            FieldInfo playerObjectListField = typeof(ClientSimNetworkingUtilities).GetField(
                "_playerObjectList", BindingFlags.Static | BindingFlags.NonPublic);
            object templateOnlyList = playerObjectListField?.GetValue(null);
            playerObjectListField?.SetValue(null, null);
#if VRC_ENABLE_PLAYER_PERSISTENCE
            HashSet<GameObject> bakedObjects = new HashSet<GameObject>();
            foreach (VRC.SDKBase.Network.NetworkIDPair bakedPair in descriptor.NetworkIDCollection)
            {
                if (bakedPair.gameObject != null)
                {
                    bakedObjects.Add(bakedPair.gameObject);
                }
            }
#endif
            try
            {
                ClientSimNetworkingUtilities.ConfigureNetworkOnScene(descriptor);
            }
            finally
            {
                if (templateOnlyList != null)
                {
                    playerObjectListField.SetValue(null, templateOnlyList);
                }
            }

#if VRC_ENABLE_PLAYER_PERSISTENCE
            // ConfigureNetworkOnScene's id-assignment pass appends every INetworkID object
            // it finds to the descriptor's NetworkIDCollection — including per-player
            // VRCPlayerObject copies that already exist by now (its persistence exclusion
            // only covers the view-attach loop). Registering a copy here would key it by a
            // wrong or soon-to-be-renumbered id and shadow the real per-player entry, so
            // prune those runtime additions. The baked template entries must stay:
            // SetupPlayerPersistence looks them up for every later spawn.
            descriptor.NetworkIDCollection.RemoveAll(p =>
                p.gameObject != null &&
                !bakedObjects.Contains(p.gameObject) &&
                p.gameObject.GetComponentInParent<VRC.SDK3.Components.VRCPlayerObject>(true) != null);
#endif

            // Note: the views carry DontSave hide flags, which FindObjectsByType skips,
            // so enumerate through the descriptor's id collection instead.
            foreach (VRC.SDKBase.Network.NetworkIDPair pair in descriptor.NetworkIDCollection)
            {
                if (pair.gameObject == null || !pair.gameObject.TryGetComponent(out ClientSimNetworkingView view))
                {
                    continue;
                }

#if VRC_ENABLE_PLAYER_PERSISTENCE
                // Per-player VRCPlayerObject copies register through RegisterRuntimeHierarchy
                // with their per-player ids; never key them by scene-collection entries.
                if (pair.gameObject.GetComponentInParent<VRC.SDK3.Components.VRCPlayerObject>(true) != null)
                {
                    continue;
                }
#endif
                int networkId = view.GetNetworkId();
                if (networkId <= 0)
                {
                    continue;
                }

                _objects[networkId] = view.gameObject;

                if (!view.TryGetComponent(out ClientSimNetworkIdHolder holder))
                {
                    holder = view.gameObject.AddComponent<ClientSimNetworkIdHolder>();
                    holder.SetNetworkView(view);
                    holder.SetNetworkComponents();
                    view.AddNetworkedObject(holder);
                }

                if (holder.GetNetworkComponentCount() == 0)
                {
                    // Nothing syncable on this object (e.g. all UdonBehaviours are SyncType.None).
                    continue;
                }

                if (_holders.ContainsKey(networkId))
                {
                    MultiSimLog.Warn($"Duplicate network id {networkId} on '{view.gameObject.name}'. Skipping.");
                    continue;
                }

                _holders[networkId] = holder;
                PrimeHolderData(holder);
            }

            MultiSimLog.Verbose($"Registered {_holders.Count} networked objects.");
        }

        /// <summary>
        /// Registers runtime-instantiated networked objects (e.g. VRCPlayerObject copies
        /// created per player). Overwrites destroyed entries so player-id reuse after a
        /// leave/rejoin works.
        /// </summary>
        public void RegisterRuntimeHierarchy(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            foreach (ClientSimNetworkingView view in root.GetComponentsInChildren<ClientSimNetworkingView>(true))
            {
                int networkId = view.GetNetworkId();
                if (networkId <= 0)
                {
                    continue;
                }

                _objects[networkId] = view.gameObject;

                if (!view.TryGetComponent(out ClientSimNetworkIdHolder holder) ||
                    holder.GetNetworkComponentCount() == 0)
                {
                    continue;
                }

                if (_holders.TryGetValue(networkId, out ClientSimNetworkIdHolder existing) &&
                    existing != null && existing != holder)
                {
                    MultiSimLog.Warn($"Duplicate network id {networkId} on '{view.gameObject.name}'. Skipping.");
                    continue;
                }

                _holders[networkId] = holder;
                PrimeHolderData(holder);
            }
        }

        /// <summary>
        /// Fills the holder's data list with the current values once so ClientSim's
        /// ClientSimNetworkIdHolder inspector builds one field row per component up front.
        /// The inspector indexes its rows by component position on later update events and
        /// throws if the data list was still empty when the inspector was first drawn.
        /// </summary>
        private static void PrimeHolderData(ClientSimNetworkIdHolder holder)
        {
            // Passing the gameObject bypasses the manual-sync early-out in Encode.
            holder.Encode(holder.gameObject);
        }

        public bool TryGetHolder(int networkId, out ClientSimNetworkIdHolder holder)
        {
            if (_holders.TryGetValue(networkId, out holder) && holder != null)
            {
                return true;
            }
            holder = null;
            return false;
        }

        public bool TryGetGameObject(int networkId, out GameObject obj)
        {
            if (_objects.TryGetValue(networkId, out obj) && obj != null)
            {
                return true;
            }
            obj = null;
            return false;
        }

        public bool TryGetNetworkId(GameObject obj, out int networkId)
        {
            networkId = 0;
            if (obj == null || !obj.TryGetComponent(out ClientSimNetworkingView view))
            {
                return false;
            }

            networkId = view.GetNetworkId();
            return networkId > 0;
        }

        public static int GetOwnerId(GameObject obj)
        {
            VRCPlayerApi owner = Networking.GetOwner(obj);
            return owner?.playerId ?? -1;
        }
    }
}
#endif
