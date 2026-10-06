#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.ClientSim;
#if VRC_ENABLE_PLAYER_PERSISTENCE
using VRC.SDK3.ClientSim.Persistence;
#endif
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace MultiSim
{
    /// <summary>
    /// Reflection bridge into ClientSim internals that are not exposed publicly.
    /// Everything here is pinned against com.vrchat.worlds 3.10.x. If a lookup fails after an
    /// SDK update, InitializeOrThrow reports exactly which member went missing.
    /// </summary>
    internal static class MultiSimReflect
    {
        private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        // ClientSimMain
        private static FieldInfo _mainInstanceField;
        private static FieldInfo _mainPlayerManagerField;
        private static FieldInfo _mainUdonManagerField;
        private static FieldInfo _mainSceneManagerField;
        private static FieldInfo _mainSettingsField;
        private static FieldInfo _mainPlayerField;
        private static FieldInfo _mainIsReadyField;
        private static FieldInfo _mainEventDispatcherField;
        private static FieldInfo _mainStackedCameraField;
        private static MethodInfo _stackedCameraReadyMethod;

        // ClientSimPlayerManager
        private static FieldInfo _pmPlayerIdsField;
        private static FieldInfo _pmPlayersField;
        private static FieldInfo _pmLocalPlayerIdField;
        private static FieldInfo _pmMasterIdField;
        private static FieldInfo _pmNextPlayerIdField;

        // ClientSimSceneManager
        private static MethodInfo _sceneResetSpawnOrderMethod;

        // ClientSimPlayer
        private static FieldInfo _playerIsInstanceOwnerField;
        private static FieldInfo _playerIsVrcPlusField;

        // ClientSimSettings
        private static FieldInfo _settingsInstanceField;

        // VRCPlayerApi
        private static FieldInfo _playerApiMPlayerIdField;

        // NetworkCalling (VRCSDK3.dll)
        private static PropertyInfo _sendNetworkEventProxyProperty;
        private static PropertyInfo _inNetworkCallProperty;
        private static PropertyInfo _callingPlayerProperty;

        // UdonBehaviour
        private static FieldInfo _udonBehaviourProgramField;

        // ClientSimNetworkIdHolder
        private static FieldInfo _encodeDecodersField;

        // ClientSimStationHelper
        private static FieldInfo _stationUsingPlayerField;

        private static bool _initialized;

        public static void InitializeOrThrow()
        {
            if (_initialized)
            {
                return;
            }

            Type mainType = typeof(ClientSimMain);
            _mainInstanceField = Require(mainType.GetField("_instance", AnyStatic), "ClientSimMain._instance");
            _mainPlayerManagerField = Require(mainType.GetField("_playerManager", AnyInstance), "ClientSimMain._playerManager");
            _mainUdonManagerField = Require(mainType.GetField("_udonManager", AnyInstance), "ClientSimMain._udonManager");
            _mainSceneManagerField = Require(mainType.GetField("_sceneManager", AnyInstance), "ClientSimMain._sceneManager");
            _mainSettingsField = Require(mainType.GetField("_settings", AnyInstance), "ClientSimMain._settings");
            _mainPlayerField = Require(mainType.GetField("_player", AnyInstance), "ClientSimMain._player");
            _mainIsReadyField = Require(mainType.GetField("_isReady", AnyInstance), "ClientSimMain._isReady");
            _mainEventDispatcherField = Require(mainType.GetField("_eventDispatcher", AnyInstance), "ClientSimMain._eventDispatcher");
            _mainStackedCameraField = mainType.GetField("stackedCameraSystem", AnyInstance); // optional
            if (_mainStackedCameraField != null)
            {
                _stackedCameraReadyMethod = _mainStackedCameraField.FieldType.GetMethod("Ready", AnyInstance);
            }

            Type pmType = typeof(ClientSimPlayerManager);
            _pmPlayerIdsField = Require(pmType.GetField("_playerIDs", AnyInstance), "ClientSimPlayerManager._playerIDs");
            _pmPlayersField = Require(pmType.GetField("_players", AnyInstance), "ClientSimPlayerManager._players");
            _pmLocalPlayerIdField = Require(pmType.GetField("_localPlayerID", AnyInstance), "ClientSimPlayerManager._localPlayerID");
            _pmMasterIdField = Require(pmType.GetField("_masterID", AnyInstance), "ClientSimPlayerManager._masterID");
            _pmNextPlayerIdField = Require(pmType.GetField("_nextPlayerID", AnyInstance), "ClientSimPlayerManager._nextPlayerID");

            _sceneResetSpawnOrderMethod = typeof(ClientSimSceneManager).GetMethod("ResetSpawnOrder", AnyInstance); // optional

            Type playerType = typeof(ClientSimPlayer);
            _playerIsInstanceOwnerField = playerType.GetField("isInstanceOwner", AnyInstance);
            _playerIsVrcPlusField = playerType.GetField("isVRCPlus", AnyInstance);

            _settingsInstanceField = Require(typeof(ClientSimSettings).GetField("_instance", AnyStatic), "ClientSimSettings._instance");

            _playerApiMPlayerIdField = Require(typeof(VRCPlayerApi).GetField("mPlayerId", AnyInstance), "VRCPlayerApi.mPlayerId");

            Type networkCallingType = FindType("VRC.SDK3.UdonNetworkCalling.NetworkCalling");
            if (networkCallingType != null)
            {
                _sendNetworkEventProxyProperty = networkCallingType.GetProperty("SendCustomNetworkEventProxy", AnyStatic);
                _inNetworkCallProperty = networkCallingType.GetProperty("InNetworkCall", AnyStatic);
                _callingPlayerProperty = networkCallingType.GetProperty("CallingPlayer", AnyStatic);
            }
            if (_sendNetworkEventProxyProperty == null || _inNetworkCallProperty == null || _callingPlayerProperty == null)
            {
                throw new InvalidOperationException(
                    "MultiSim: NetworkCalling members not found. Custom network event forwarding is unavailable with this SDK version.");
            }

            _udonBehaviourProgramField = Require(
                typeof(UdonBehaviour).GetField("_program", AnyInstance), "UdonBehaviour._program");
            _encodeDecodersField = Require(
                typeof(ClientSimNetworkIdHolder).GetField("encodeDecoders", AnyStatic), "ClientSimNetworkIdHolder.encodeDecoders");

            // Optional: only used to mark stations as occupied by remote players.
            _stationUsingPlayerField = typeof(ClientSimStationHelper).GetField("_usingPlayer", AnyInstance);

#if VRC_ENABLE_PLAYER_PERSISTENCE
            _playerDataObjectField = Require(
                playerType.GetField("PlayerDataObject", AnyInstance), "ClientSimPlayer.PlayerDataObject");
            Type storageType = typeof(ClientSimPlayerDataStorage);
            _pdFlushLocalInfoChangesMethod = Require(
                storageType.GetMethod("FlushLocalInfoChanges", AnyInstance),
                "ClientSimPlayerDataStorage.FlushLocalInfoChanges");
            _pdLeDataField = Require(
                storageType.GetField("leData", AnyInstance), "ClientSimPlayerDataStorage.leData");
#endif

            _initialized = true;
        }

#if VRC_ENABLE_PLAYER_PERSISTENCE
        private static FieldInfo _playerDataObjectField;
        private static MethodInfo _pdFlushLocalInfoChangesMethod;
        private static FieldInfo _pdLeDataField;

        public static ClientSimPlayerDataStorage GetPlayerDataStorage(ClientSimPlayer player)
        {
            return (ClientSimPlayerDataStorage)_playerDataObjectField.GetValue(player);
        }

        /// <summary>Queues an OnPlayerDataUpdated commit for changes staged with flushChanges:false.</summary>
        public static void FlushPlayerDataChanges(ClientSimPlayerDataStorage storage)
        {
            _pdFlushLocalInfoChangesMethod.Invoke(storage, null);
        }

        public static System.Collections.Generic.Dictionary<string, ClientSimPlayerDataPair> GetPlayerDataDictionary(
            ClientSimPlayerDataStorage storage)
        {
            return (System.Collections.Generic.Dictionary<string, ClientSimPlayerDataPair>)_pdLeDataField.GetValue(storage);
        }
#endif

        private static T Require<T>(T member, string description) where T : MemberInfo
        {
            if (member == null)
            {
                throw new InvalidOperationException(
                    $"MultiSim: Required ClientSim member '{description}' was not found. " +
                    "The installed VRChat SDK version is likely incompatible (expected 3.10.x).");
            }
            return member;
        }

        private static Type FindType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = assembly.GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }
            return null;
        }

        #region ClientSimMain accessors

        public static ClientSimMain GetMainInstance()
        {
            return (ClientSimMain)_mainInstanceField.GetValue(null);
        }

        public static ClientSimPlayerManager GetPlayerManager(ClientSimMain main)
        {
            return (ClientSimPlayerManager)_mainPlayerManagerField.GetValue(main);
        }

        public static ClientSimUdonManager GetUdonManager(ClientSimMain main)
        {
            return (ClientSimUdonManager)_mainUdonManagerField.GetValue(main);
        }

        public static IClientSimSceneManager GetSceneManager(ClientSimMain main)
        {
            return (IClientSimSceneManager)_mainSceneManagerField.GetValue(main);
        }

        public static ClientSimSettings GetSettings(ClientSimMain main)
        {
            return (ClientSimSettings)_mainSettingsField.GetValue(main);
        }

        public static ClientSimPlayer GetLocalClientSimPlayer(ClientSimMain main)
        {
            return (ClientSimPlayer)_mainPlayerField.GetValue(main);
        }

        public static IClientSimEventDispatcher GetEventDispatcher(ClientSimMain main)
        {
            return (IClientSimEventDispatcher)_mainEventDispatcherField.GetValue(main);
        }

        public static void SetMainReady(ClientSimMain main)
        {
            _mainIsReadyField.SetValue(main, true);
        }

        public static void CallStackedCameraReady(ClientSimMain main)
        {
            if (_mainStackedCameraField == null || _stackedCameraReadyMethod == null)
            {
                return;
            }

            try
            {
                object stackedCamera = _mainStackedCameraField.GetValue(main);
                if (stackedCamera != null)
                {
                    _stackedCameraReadyMethod.Invoke(stackedCamera, null);
                }
            }
            catch (Exception e)
            {
                MultiSimLog.Warn($"Failed to notify stacked camera system: {e.Message}");
            }
        }

        #endregion

        #region Player manager accessors

        public static void SetNextPlayerId(ClientSimPlayerManager playerManager, int id)
        {
            _pmNextPlayerIdField.SetValue(playerManager, id);
        }

        public static int GetNextPlayerId(ClientSimPlayerManager playerManager)
        {
            return (int)_pmNextPlayerIdField.GetValue(playerManager);
        }

        public static void SetMasterId(ClientSimPlayerManager playerManager, int id)
        {
            _pmMasterIdField.SetValue(playerManager, id);
        }

        /// <summary>Reassigns an already-registered player to a new player id.</summary>
        public static void RemapPlayerId(ClientSimPlayerManager playerManager, VRCPlayerApi player, int newId)
        {
            var playerIds = (Dictionary<VRCPlayerApi, int>)_pmPlayerIdsField.GetValue(playerManager);
            var players = (Dictionary<int, VRCPlayerApi>)_pmPlayersField.GetValue(playerManager);

            int oldId = playerIds[player];
            if (oldId == newId)
            {
                return;
            }

            players.Remove(oldId);
            players[newId] = player;
            playerIds[player] = newId;

            if (player.isLocal)
            {
                _pmLocalPlayerIdField.SetValue(playerManager, newId);
            }

            SetCachedPlayerId(player, newId);

            if (player.gameObject != null)
            {
                player.gameObject.name = $"[{newId}] {(player.isLocal ? "Local" : "Remote")} Player";
            }
        }

        /// <summary>VRCPlayerApi caches its id in a private field; keep it in sync after remapping.</summary>
        public static void SetCachedPlayerId(VRCPlayerApi player, int id)
        {
            _playerApiMPlayerIdField.SetValue(player, id);
        }

        public static void SortAllPlayers(Comparison<VRCPlayerApi> comparison)
        {
            VRCPlayerApi.AllPlayers.Sort(comparison);
        }

        #endregion

        #region Misc ClientSim accessors

        public static void ResetSpawnOrder(IClientSimSceneManager sceneManager)
        {
            if (_sceneResetSpawnOrderMethod != null && sceneManager is ClientSimSceneManager concrete)
            {
                try
                {
                    _sceneResetSpawnOrderMethod.Invoke(concrete, null);
                }
                catch (Exception e)
                {
                    MultiSimLog.Warn($"ResetSpawnOrder failed: {e.Message}");
                }
            }
        }

        public static void SetLocalPlayerFlags(ClientSimPlayer player, bool isInstanceOwner, bool isVrcPlus)
        {
            _playerIsInstanceOwnerField?.SetValue(player, isInstanceOwner);
            _playerIsVrcPlusField?.SetValue(player, isVrcPlus);
        }

        /// <summary>
        /// Replaces the cached ClientSimSettings singleton. Passing null makes the next
        /// ClientSimSettings.Instance access reload the real values from EditorPrefs.
        /// </summary>
        public static void SetClientSimSettingsInstance(ClientSimSettings settings)
        {
            FieldInfo field = typeof(ClientSimSettings).GetField("_instance", AnyStatic);
            field?.SetValue(null, settings);
        }

        #endregion

        #region Udon program access

        public static VRC.Udon.Common.Interfaces.IUdonProgram GetUdonProgram(UdonBehaviour udonBehaviour)
        {
            return (VRC.Udon.Common.Interfaces.IUdonProgram)_udonBehaviourProgramField.GetValue(udonBehaviour);
        }

        /// <summary>
        /// Replaces the codec ClientSimNetworkIdHolder uses for a component type.
        /// Returns the codec that was previously registered (for restoring on teardown).
        /// </summary>
        public static VRC.SDK3.ClientSim.Interfaces.IClientSimEncodeDecoder SwapEncodeDecoder(
            Type componentType, VRC.SDK3.ClientSim.Interfaces.IClientSimEncodeDecoder replacement)
        {
            var decoders = (Dictionary<string, VRC.SDK3.ClientSim.Interfaces.IClientSimEncodeDecoder>)
                _encodeDecodersField.GetValue(null);
            decoders.TryGetValue(componentType.FullName, out var previous);
            decoders[componentType.FullName] = replacement;
            return previous;
        }

        #endregion

        #region Stations

        /// <summary>
        /// Marks a station as occupied (or free when player is null) so ClientSim's
        /// occupancy checks (IsOccupied/GetCurrentSittingPlayer) see remote sitters.
        /// The helper's own EnterStation rejects non-local players, hence reflection.
        /// </summary>
        public static void SetStationOccupant(GameObject stationObject, VRCPlayerApi player)
        {
            if (_stationUsingPlayerField == null || stationObject == null ||
                !stationObject.TryGetComponent(out ClientSimStationHelper helper))
            {
                return;
            }
            _stationUsingPlayerField.SetValue(helper, player);
        }

        public static VRCPlayerApi GetStationOccupant(GameObject stationObject)
        {
            if (_stationUsingPlayerField == null || stationObject == null ||
                !stationObject.TryGetComponent(out ClientSimStationHelper helper))
            {
                return null;
            }
            return (VRCPlayerApi)_stationUsingPlayerField.GetValue(helper);
        }

        #endregion

        #region NetworkCalling hooks

        /// <summary>
        /// Appends a handler to NetworkCalling.SendCustomNetworkEventProxy. The delegate type is
        /// internal to VRCSDK3, so the handler method is bound through Delegate.CreateDelegate.
        /// The handler must have the signature:
        /// (IUdonEventReceiver receiver, NetworkEventTarget target, string eventName, Memory&lt;object&gt; parameters)
        /// </summary>
        public static Delegate AddSendCustomNetworkEventHandler(MethodInfo handlerMethod)
        {
            Type delegateType = _sendNetworkEventProxyProperty.PropertyType;
            Delegate handler = Delegate.CreateDelegate(delegateType, handlerMethod);
            Delegate current = (Delegate)_sendNetworkEventProxyProperty.GetValue(null);
            _sendNetworkEventProxyProperty.SetValue(null, Delegate.Combine(current, handler));
            return handler;
        }

        public static void RemoveSendCustomNetworkEventHandler(Delegate handler)
        {
            if (handler == null)
            {
                return;
            }
            Delegate current = (Delegate)_sendNetworkEventProxyProperty.GetValue(null);
            _sendNetworkEventProxyProperty.SetValue(null, Delegate.Remove(current, handler));
        }

        public static void SetNetworkCallingContext(bool inNetworkCall, VRCPlayerApi callingPlayer)
        {
            _inNetworkCallProperty.SetValue(null, inNetworkCall);
            _callingPlayerProperty.SetValue(null, callingPlayer);
        }

        #endregion
    }
}
#endif
