#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using VRC.SDK3.ClientSim;
#if VRC_ENABLE_PLAYER_PERSISTENCE
using VRC.SDK3.ClientSim.Persistence;
#endif
using VRC.SDK3.ClientSim.Interfaces;
using VRC.SDK3.Data;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;
using VRC.Udon.Common;
using VRC.Udon.Common.Interfaces;

namespace MultiSim
{
    /// <summary>
    /// Multiplayer simulation for ClientSim across ParrelSync editor instances.
    ///
    /// The ParrelSync original acts as host (player id 1, master); clones join as clients over
    /// localhost TCP. ClientSim starts normally so all of its SDK hooks are in place, but its
    /// "ready" step (Udon start + OnPlayerJoined dispatch) is held back until the network
    /// handshake finishes, so every instance agrees on player ids and the master before any
    /// Udon code runs. Synced variables, custom network events, ownership, VRCObjectSync and
    /// player positions are then exchanged through ClientSim's own encode/decode pipeline.
    /// </summary>
    [AddComponentMenu("")]
    internal class MultiSimCore : MonoBehaviour
    {
        private const string ClientSimSettingsPrefsKey = "com.vrchat.clientsim.settings";

        private static MultiSimCore _instance;
        private static ClientSimSettings _forcedSettings;

        private MultiSimTransport _transport;
        private readonly MultiSimRegistry _registry = new MultiSimRegistry();

        private ClientSimMain _main;
        private ClientSimPlayerManager _playerManager;

        private bool _isHost;
        private bool _ready;
        private bool _sessionActive; // true while connected to at least one peer
        private int _localPlayerId = 1;
        private int _nextGlobalPlayerId = 2;
        private string _localPlayerName;

        // Join order shared by all instances; index 0 is always the host/master.
        private readonly List<int> _joinOrder = new List<int>();

        // Host: connection id -> player id (assigned after HELLO).
        private readonly Dictionary<int, int> _connectionPlayers = new Dictionary<int, int>();
        private readonly List<(int connId, DataDictionary envelope)> _pendingHellos =
            new List<(int, DataDictionary)>();

        // Client: world messages buffered until the ready sequence finished.
        private readonly List<DataDictionary> _pendingWorldMessages = new List<DataDictionary>();
        private bool _welcomeReceived;

        private readonly List<UdonBehaviour> _manualSerializationQueue = new List<UdonBehaviour>();
        private readonly List<UdonBehaviour> _manualSerializationProcessing = new List<UdonBehaviour>();
        private bool _applyingRemoteOwner;

        // Station seats by player id. The local player's seat is tracked too (for host
        // snapshots); pinning and Udon events only apply to remote players.
        private class SeatInfo
        {
            public int PlayerId;
            public int NetworkId;
            public GameObject StationObject;
            public VRC.SDK3.Components.VRCStation Station;
        }
        private readonly Dictionary<int, SeatInfo> _seatsByPlayer = new Dictionary<int, SeatInfo>();
        private IClientSimEventDispatcher _dispatcher;
        private bool _localSeated;

        private Delegate _sendEventHandler;
        private bool _hooksInstalled;
        private IClientSimEncodeDecoder _originalUdonCodec;
        private bool _udonCodecSwapped;

        private float _nextContinuousSync;
        private float _nextPlayerPosSend;
        private Vector3 _lastSentPosition;
        private Quaternion _lastSentRotation;

        #region Bootstrap

        [InitializeOnLoadMethod]
        private static void RegisterEditorHooks()
        {
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                {
                    // Never leave a runtime-modified settings object cached for the settings window.
                    MultiSimReflect.SetClientSimSettingsInstance(null);
                }
            };
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void EarlyInit()
        {
            _forcedSettings = null;
            // Clear any stale override (matters when domain reload is disabled).
            MultiSimReflect.SetClientSimSettingsInstance(null);
            MultiSimUdonCodec.ClearDeferred();

            if (!MultiSimPrefs.Enabled)
            {
                return;
            }

            try
            {
                MultiSimReflect.InitializeOrThrow();
            }
            catch (Exception e)
            {
                MultiSimLog.Error($"Disabled: {e.Message}");
                return;
            }

            ClientSimSettings settings = LoadRealClientSimSettings();
            if (!settings.enableClientSim)
            {
                MultiSimLog.Warn("ClientSim is disabled in its settings; multiplayer simulation will not run.");
                return;
            }

            // Hold ClientSim's ready step until the handshake decides player ids.
            settings.initializationDelay = float.PositiveInfinity;
            // The id fixup logic assumes the local player spawns first.
            settings.localPlayerIsMaster = true;

            if (string.IsNullOrEmpty(settings.customLocalPlayerName))
            {
                if (MultiSimPrefs.IsCloneInstance())
                {
                    string arg = MultiSimPrefs.CloneArgument();
                    settings.customLocalPlayerName = "Player-" + (string.IsNullOrEmpty(arg) ? "Clone" : arg);
                }
                else
                {
                    settings.customLocalPlayerName = "Player-Host";
                }
            }

            MultiSimReflect.SetClientSimSettingsInstance(settings);
            _forcedSettings = settings;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LateInit()
        {
            if (_forcedSettings == null)
            {
                return;
            }

            try
            {
                if (!ClientSimMain.HasInstance())
                {
                    MultiSimLog.Warn("ClientSim did not start (missing scene descriptor?). Multiplayer simulation skipped.");
                    ReleaseClientSimHold();
                    return;
                }

                GameObject go = new GameObject("__MultiSim");
                DontDestroyOnLoad(go);
                if (go.AddComponent<MultiSimCore>() == null)
                {
                    // AddComponent can fail without throwing (e.g. editor-assembly scripts).
                    throw new InvalidOperationException("AddComponent<MultiSimCore> returned null.");
                }
            }
            catch (Exception e)
            {
                MultiSimLog.Error($"Failed to start multiplayer simulation: {e}");
                ReleaseClientSimHold();
            }
        }

        /// <summary>Lets ClientSim initialize normally in case MultiSim cannot run.</summary>
        private static void ReleaseClientSimHold()
        {
            if (_forcedSettings != null)
            {
                _forcedSettings.initializationDelay = 0f;
                _forcedSettings = null;
            }
        }

        private static ClientSimSettings LoadRealClientSimSettings()
        {
            ClientSimSettings settings = new ClientSimSettings();
            string json = EditorPrefs.GetString(ClientSimSettingsPrefsKey, JsonUtility.ToJson(settings, false));
            JsonUtility.FromJsonOverwrite(json, settings);
            return settings;
        }

        #endregion

        #region Lifecycle

        private void Awake()
        {
            _instance = this;

            _main = MultiSimReflect.GetMainInstance();
            _playerManager = MultiSimReflect.GetPlayerManager(_main);
            _isHost = !MultiSimPrefs.IsCloneInstance();
            _localPlayerName = _forcedSettings != null ? _forcedSettings.customLocalPlayerName : "Player";

            // ClientSim's own UdonBehaviour codec corrupts UdonSharp heap slots (see MultiSimUdonCodec).
            _originalUdonCodec = MultiSimReflect.SwapEncodeDecoder(typeof(UdonBehaviour), new MultiSimUdonCodec());
            _udonCodecSwapped = true;
            // The matching inspector renderer (ClientSim's assumes the original codec format).
            MultiSimUdonCodecEditor.TryRegister();

            InstallHooks();

            // Station enter/exit for the LOCAL player is reported through ClientSim's dispatcher.
            _dispatcher = MultiSimReflect.GetEventDispatcher(_main);
            _dispatcher.Subscribe<ClientSimOnPlayerEnteredStationEvent>(OnLocalPlayerEnteredStation);
            _dispatcher.Subscribe<ClientSimOnPlayerExitedStationEvent>(OnLocalPlayerExitedStation);
#if VRC_ENABLE_PLAYER_PERSISTENCE
            _dispatcher.Subscribe<ClientSimOnPlayerDataUpdatedEvent>(OnPlayerDataUpdated);
#endif

            _transport = new MultiSimTransport();
            if (_isHost)
            {
                if (_transport.StartHost(MultiSimPrefs.Port))
                {
                    _sessionActive = true;
                    _joinOrder.Add(1);
                    StartCoroutine(HostStartup());
                }
                else
                {
                    MultiSimLog.Warn("Falling back to single-player ClientSim.");
                    StartCoroutine(StandaloneStartup());
                }
            }
            else
            {
                MultiSimLog.Info($"Clone detected - joining host on port {MultiSimPrefs.Port}...");
                _transport.ConnectToHostAsync(MultiSimPrefs.Port, MultiSimPrefs.ConnectTimeout);
                StartCoroutine(ClientHandshakeWatchdog());
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }

#if VRC_ENABLE_PLAYER_PERSISTENCE
            // Adopt this session's persistence saves as the editor's stable files.
            MultiSimPersistence.SaveBack(_localPlayerId);
#endif

            RemoveHooks();
            if (_dispatcher != null)
            {
                try
                {
                    _dispatcher.Unsubscribe<ClientSimOnPlayerEnteredStationEvent>(OnLocalPlayerEnteredStation);
                    _dispatcher.Unsubscribe<ClientSimOnPlayerExitedStationEvent>(OnLocalPlayerExitedStation);
#if VRC_ENABLE_PLAYER_PERSISTENCE
                    _dispatcher.Unsubscribe<ClientSimOnPlayerDataUpdatedEvent>(OnPlayerDataUpdated);
#endif
                }
                catch (Exception)
                {
                    // Dispatcher may already be disposed during teardown.
                }
                _dispatcher = null;
            }
            if (_udonCodecSwapped && _originalUdonCodec != null)
            {
                MultiSimReflect.SwapEncodeDecoder(typeof(UdonBehaviour), _originalUdonCodec);
                _udonCodecSwapped = false;
            }
            _transport?.Dispose();
            MultiSimReflect.SetClientSimSettingsInstance(null);
            _forcedSettings = null;
        }

        private IEnumerator StandaloneStartup()
        {
#if VRC_ENABLE_PLAYER_PERSISTENCE
            MultiSimPersistence.StageForSession(1);
#endif
            yield return StartCoroutine(ReadySequence());
            _ready = true;
        }

        private IEnumerator HostStartup()
        {
            // Give ClientSim's own Start() a frame to run first.
            yield return null;
            _registry.BuildFromScene();
#if VRC_ENABLE_PLAYER_PERSISTENCE
            // Before ReadySequence dispatches the local join (= restore decode).
            MultiSimPersistence.StageForSession(1);
#endif
            yield return StartCoroutine(ReadySequence());
            _ready = true;

#if VRC_ENABLE_PLAYER_PERSISTENCE
            // The host's own VRCPlayerObject copies (id 1, already correct).
            RegisterPlayerObjects(_playerManager.LocalPlayer());
#endif

            // Handle clients that connected while we were getting ready.
            foreach ((int connId, DataDictionary envelope) in _pendingHellos)
            {
                HandleHello(connId, envelope);
            }
            _pendingHellos.Clear();
        }

        private IEnumerator ClientHandshakeWatchdog()
        {
            yield return null;
            _registry.BuildFromScene();

            float deadline = Time.realtimeSinceStartup + MultiSimPrefs.ConnectTimeout + 5f;
            while (!_welcomeReceived && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            if (!_welcomeReceived && !_ready)
            {
                MultiSimLog.Warn("Could not join a host in time - falling back to single-player ClientSim.");
                _sessionActive = false;
#if VRC_ENABLE_PLAYER_PERSISTENCE
                MultiSimPersistence.StageForSession(1);
#endif
                yield return StartCoroutine(ReadySequence());
                _ready = true;
            }
        }

        /// <summary>
        /// Replicates the tail of ClientSimMain.InitializeClientSim, which is being held back
        /// by the infinite initializationDelay: enable the player, mark ClientSim ready,
        /// start Udon and dispatch the queued OnPlayerJoined events.
        /// </summary>
        private IEnumerator ReadySequence()
        {
            ClientSimSettings settings = MultiSimReflect.GetSettings(_main);
            ClientSimPlayer localPlayer = MultiSimReflect.GetLocalClientSimPlayer(_main);
            IClientSimSceneManager sceneManager = MultiSimReflect.GetSceneManager(_main);

            if (settings.spawnPlayer && localPlayer != null)
            {
                // Only the host owns the instance in a multi-editor session.
                bool isInstanceOwner = _sessionActive ? _isHost : settings.isInstanceOwner;
                MultiSimReflect.SetLocalPlayerFlags(localPlayer, isInstanceOwner, settings.isVRCPlus);
                MultiSimReflect.ResetSpawnOrder(sceneManager);
                localPlayer.EnablePlayer(sceneManager.GetSpawnPoint(false), sceneManager.GetSpawnRadius());
            }

            MultiSimReflect.SetMainReady(_main);

            // Runtime-instantiated VRCPlayerObject copies (created when players spawn
            // before this sequence, e.g. remotes from the welcome message) carry a
            // cloned ClientSimUdonHelper whose non-serialized _udonBehaviour is null.
            // Its own Start() would destroy it, but OnClientSimReady can iterate it
            // first and throw. Sweep uninitialized helpers here, mirroring
            // ClientSimUdonHelper.Start's cleanup.
            // Resources.FindObjectsOfTypeAll, not FindObjectsByType: these helpers carry
            // HideFlags.DontSave, which FindObjectsByType silently excludes.
            foreach (ClientSimUdonHelper helper in Resources.FindObjectsOfTypeAll<ClientSimUdonHelper>())
            {
                if (EditorUtility.IsPersistent(helper))
                {
                    continue;
                }
                if (helper.GetUdonBehaviour() == null)
                {
                    MultiSimLog.Verbose($"Destroying stale uninitialized ClientSimUdonHelper on " +
                                        $"'{helper.gameObject.name}'.");
                    DestroyImmediate(helper);
                }
            }

            ClientSimUdonManager udonManager = MultiSimReflect.GetUdonManager(_main);
            yield return StartCoroutine(udonManager.OnClientSimReady());

            _playerManager.OnClientSimReady();
            MultiSimReflect.GetEventDispatcher(_main).SendEvent(new ClientSimReadyEvent());
            MultiSimReflect.CallStackedCameraReady(_main);

            VRCPlayerApi local = _playerManager.LocalPlayer();
            _localPlayerId = local != null ? local.playerId : 1;

            MultiSimLog.Info($"Ready. Role: {(_isHost ? "HOST" : "CLIENT")}, local player id: {_localPlayerId}" +
                             (_sessionActive ? "" : " (single-player fallback)"));
        }

        #endregion

        #region Update loops

        private void Update()
        {
            if (_transport == null)
            {
                return;
            }

            while (_transport.TryDequeueEvent(out TransportEvent evt))
            {
                switch (evt.Type)
                {
                    case TransportEventType.Connected:
                        OnPeerConnected(evt.ConnectionId);
                        break;
                    case TransportEventType.Disconnected:
                        OnPeerDisconnected(evt.ConnectionId);
                        break;
                    case TransportEventType.Message:
                        OnMessage(evt.ConnectionId, evt.Message);
                        break;
                }
            }

            if (!_ready || !_sessionActive)
            {
                return;
            }

#if VRC_ENABLE_PLAYER_PERSISTENCE
            if (_pendingPlayerDataBroadcast)
            {
                SendLocalPlayerData();
            }
#endif

            float now = Time.unscaledTime;
            if (now >= _nextContinuousSync)
            {
                _nextContinuousSync = now + MultiSimPrefs.ContinuousSyncInterval;
                SendContinuousUpdates();
            }

            if (now >= _nextPlayerPosSend)
            {
                _nextPlayerPosSend = now + MultiSimPrefs.PlayerPosInterval;
                SendLocalPlayerPosition();
            }
        }

        private void LateUpdate()
        {
            if (!_ready)
            {
                return;
            }

            PinSeatedRemotePlayers();

            MultiSimUdonCodec.FlushDeferred();

            // Snapshot the queue into a separate list and clear it before processing,
            // so that re-entrant RequestSerialization calls during PostEncode
            // (e.g. QvPen's batched late-sync) are added to the fresh queue and
            // processed on the next frame — matching VRChat's one-batch-per-tick model.
            if (_manualSerializationQueue.Count == 0)
            {
                return;
            }

            _manualSerializationProcessing.AddRange(_manualSerializationQueue);
            _manualSerializationQueue.Clear();

            foreach (UdonBehaviour udonBehaviour in _manualSerializationProcessing)
            {
                if (udonBehaviour == null)
                {
                    continue;
                }
                SendManualSerialization(udonBehaviour);
            }
            _manualSerializationProcessing.Clear();
        }

        #endregion

        #region Connection events

        private void OnPeerConnected(int connectionId)
        {
            if (_transport.IsHost)
            {
                MultiSimLog.Verbose($"Client connection {connectionId} established, waiting for hello.");
            }
            else
            {
                if (_ready)
                {
                    // The single-player fallback already ran; joining now would corrupt player ids.
                    MultiSimLog.Warn("Host appeared after the fallback to single-player. Restart play mode to join.");
                    _transport.Dispose();
                    return;
                }

                // Connected to host: introduce ourselves.
                DataDictionary payload = new DataDictionary();
                payload["name"] = _localPlayerName;
                payload["ver"] = MultiSimPrefs.ProtocolVersion;
                SendEnvelope("hello", payload);
                _sessionActive = true;
            }
        }

        private void OnPeerDisconnected(int connectionId)
        {
            if (connectionId == -1)
            {
                // Client-side connect failure; the watchdog coroutine handles the fallback.
                return;
            }

            if (_transport.IsHost)
            {
                if (_connectionPlayers.TryGetValue(connectionId, out int playerId))
                {
                    _connectionPlayers.Remove(connectionId);
                    MultiSimLog.Info($"Player {playerId} disconnected.");
                    RemoveRemotePlayer(playerId);

                    DataDictionary payload = new DataDictionary();
                    payload["id"] = playerId;
                    BroadcastEnvelope("leave", payload, exceptConnectionId: connectionId);
                }
                return;
            }

            // Client lost the host.
            MultiSimLog.Warn("Host disconnected - the session ends and this instance continues alone.");
            _sessionActive = false;
            if (_ready)
            {
                RemoveAllRemotePlayers();
            }
        }

        #endregion

        #region Message handling

        private void OnMessage(int connectionId, string json)
        {
            if (!MultiSimJson.TryDeserialize(json, out DataDictionary envelope) ||
                !envelope.TryGetValue("t", out DataToken typeToken))
            {
                MultiSimLog.Warn("Received malformed message.");
                return;
            }

            string type = typeToken.String;

            if (_transport.IsHost)
            {
                if (type == "hello")
                {
                    if (_ready)
                    {
                        HandleHello(connectionId, envelope);
                    }
                    else
                    {
                        _pendingHellos.Add((connectionId, envelope));
                    }
                    return;
                }

                // Relay world messages to every other client, then apply locally.
                _transport.Broadcast(json, exceptConnectionId: connectionId);
                ApplyWorldMessage(envelope);
                return;
            }

            // Client side.
            if (type == "welcome")
            {
                HandleWelcome(envelope);
                return;
            }

            if (!_ready)
            {
                _pendingWorldMessages.Add(envelope);
                return;
            }

            ApplyWorldMessage(envelope);
        }

        private void ApplyWorldMessage(DataDictionary envelope)
        {
            string type = envelope["t"].String;
            int senderId = envelope.TryGetValue("s", out DataToken senderToken) ? (int)senderToken.Double : -1;
            MultiSimLog.Verbose($"recv '{type}' from {senderId}");
            DataDictionary payload = envelope.TryGetValue("p", out DataToken payloadToken) &&
                                     payloadToken.TokenType == TokenType.DataDictionary
                ? payloadToken.DataDictionary
                : new DataDictionary();

            switch (type)
            {
                case "join":
                    HandlePlayerJoin(payload);
                    break;
                case "leave":
                    HandlePlayerLeave(payload);
                    break;
                case "snapshot":
                    HandleSnapshot(payload);
                    break;
                case "var":
                    HandleVariableSync(senderId, payload);
                    break;
                case "owner":
                    HandleOwnership(payload);
                    break;
                case "event":
                    HandleNetworkEvent(senderId, payload);
                    break;
                case "pos":
                    HandlePlayerPosition(senderId, payload);
                    break;
                case "sit":
                    ApplySeat(senderId, (int)payload["nid"].Double, fireEvents: true);
                    break;
                case "unsit":
                    ApplyUnseat(senderId, fireEvents: true, warpToExit: true);
                    break;
#if VRC_ENABLE_PLAYER_PERSISTENCE
                case "pdata":
                    ApplyPlayerData((int)payload["pid"].Double, payload["json"].String);
                    break;
#endif
                default:
                    MultiSimLog.Verbose($"Ignoring unknown message type '{type}'.");
                    break;
            }
        }

        #endregion

        #region Handshake (host)

        private void HandleHello(int connectionId, DataDictionary envelope)
        {
            DataDictionary payload = envelope.TryGetValue("p", out DataToken payloadToken) &&
                                     payloadToken.TokenType == TokenType.DataDictionary
                ? payloadToken.DataDictionary
                : new DataDictionary();

            int version = payload.TryGetValue("ver", out DataToken verToken) ? (int)verToken.Double : -1;
            if (version != MultiSimPrefs.ProtocolVersion)
            {
                MultiSimLog.Warn($"Client protocol version {version} != {MultiSimPrefs.ProtocolVersion}. Proceeding anyway.");
            }

            string name = payload.TryGetValue("name", out DataToken nameToken) && nameToken.TokenType == TokenType.String
                ? nameToken.String
                : null;

            int newPlayerId = _nextGlobalPlayerId++;
            if (string.IsNullOrEmpty(name))
            {
                name = $"Player-{newPlayerId}";
            }

            // Describe the current session (before the new player is added locally).
            DataDictionary welcome = new DataDictionary();
            welcome["id"] = newPlayerId;
            welcome["master"] = 1;
            DataList playerList = new DataList();
            foreach (int existingId in _joinOrder)
            {
                VRCPlayerApi api = _playerManager.GetPlayerByID(existingId);
                if (api == null)
                {
                    continue;
                }
                DataDictionary entry = new DataDictionary();
                entry["id"] = existingId;
                entry["name"] = api.displayName;
                entry["pos"] = api.GetPosition().GetJTokenFromVector3();
                entry["rot"] = api.GetRotation().GetJTokenFromQuaternion();
                playerList.Add(entry);
            }
            welcome["players"] = playerList;
            SendEnvelopeTo(connectionId, "welcome", welcome);

            // Spawn the remote player locally with the assigned id.
            SpawnRemotePlayerWithId(newPlayerId, name);
            _joinOrder.Add(newPlayerId);
            _connectionPlayers[connectionId] = newPlayerId;

            // Tell everyone else.
            DataDictionary joinPayload = new DataDictionary();
            joinPayload["id"] = newPlayerId;
            joinPayload["name"] = name;
            BroadcastEnvelope("join", joinPayload, exceptConnectionId: connectionId);

            // Send the world state to the newcomer.
            SendEnvelopeTo(connectionId, "snapshot", BuildSnapshot());

            MultiSimLog.Info($"Player {newPlayerId} ('{name}') joined.");
        }

        private DataDictionary BuildSnapshot()
        {
            DataDictionary snapshot = new DataDictionary();
            DataList objects = new DataList();

            foreach (KeyValuePair<int, ClientSimNetworkIdHolder> pair in _registry.Holders)
            {
                ClientSimNetworkIdHolder holder = pair.Value;
                if (holder == null)
                {
                    continue;
                }

                DataDictionary entry = new DataDictionary();
                entry["nid"] = pair.Key;
                entry["owner"] = MultiSimRegistry.GetOwnerId(holder.gameObject);

                DataList data;
                if (MultiSimRegistry.GetOwnerId(holder.gameObject) == _localPlayerId)
                {
                    // Freshly serialize objects we own (forcing manual objects too).
                    holder.PreEncode(holder.gameObject);
                    data = holder.Encode(holder.gameObject);
                    holder.PostEncode(holder.gameObject);
                }
                else
                {
                    // Latest state received from the actual owner, if any.
                    data = holder.GetData();
                }

                if (data != null && data.Count > 0)
                {
                    // Serialized to JSON before this method returns, so no defensive copy is needed.
                    entry["data"] = data;
                }

                objects.Add(entry);
            }

            snapshot["objs"] = objects;

            DataList seats = new DataList();
            foreach (SeatInfo seat in _seatsByPlayer.Values)
            {
                DataDictionary entry = new DataDictionary();
                entry["nid"] = seat.NetworkId;
                entry["pid"] = seat.PlayerId;
                seats.Add(entry);
            }
            snapshot["seats"] = seats;

#if VRC_ENABLE_PLAYER_PERSISTENCE
            // PlayerData per player. The host's stores aggregate what every editor
            // broadcast, so a late joiner gets the full picture from here.
            DataList playerData = new DataList();
            foreach (VRCPlayerApi api in VRCPlayerApi.AllPlayers)
            {
                if (!MultiSimPlayerData.TryGetStorage(api, out ClientSimPlayerDataStorage storage))
                {
                    continue;
                }

                string json = MultiSimPlayerData.Serialize(storage);
                if (string.IsNullOrEmpty(json) || json == "{}")
                {
                    continue;
                }

                DataDictionary entry = new DataDictionary();
                entry["pid"] = api.playerId;
                entry["json"] = json;
                playerData.Add(entry);
            }
            snapshot["pdata"] = playerData;
#endif

            return snapshot;
        }

        #endregion

        #region Handshake (client)

        private void HandleWelcome(DataDictionary envelope)
        {
            // _ready guards against a welcome arriving after the single-player fallback ran.
            if (_welcomeReceived || _ready)
            {
                return;
            }
            _welcomeReceived = true;

            DataDictionary payload = envelope["p"].DataDictionary;
            int myId = (int)payload["id"].Double;
            int masterId = (int)payload["master"].Double;
            DataList players = payload["players"].DataList;

            // Remap the local player (currently id 1) to the assigned id.
            VRCPlayerApi local = _playerManager.LocalPlayer();
            int previousId = local.playerId;
            MultiSimReflect.RemapPlayerId(_playerManager, local, myId);
            _localPlayerId = myId;

#if VRC_ENABLE_PLAYER_PERSISTENCE
            // The local player's VRCPlayerObject copies were instantiated with the
            // pre-remap id; renumber them before remote spawns can claim id 1's range.
            RenumberLocalPlayerObjects(local, previousId, myId);
            RegisterPlayerObjects(local);
            // Before the join dispatch triggers ClientSim's restore decode.
            MultiSimPersistence.StageForSession(myId);
#endif

            // Spawn remote players for everyone already in the session, in join order.
            _joinOrder.Clear();
            int maxId = myId;
            for (int i = 0; i < players.Count; i++)
            {
                DataDictionary entry = players[i].DataDictionary;
                int id = (int)entry["id"].Double;
                string name = entry["name"].String;
                SpawnRemotePlayerWithId(id, name);
                VRCPlayerApi api = _playerManager.GetPlayerByID(id);
                if (api != null && entry.TryGetValue("pos", out DataToken posToken))
                {
                    api.gameObject.transform.SetPositionAndRotation(
                        posToken.GetVector3(),
                        entry.TryGetValue("rot", out DataToken rotToken) ? rotToken.GetQuaternion() : Quaternion.identity);
                }
                _joinOrder.Add(id);
                maxId = Mathf.Max(maxId, id);
            }
            _joinOrder.Add(myId);

            MultiSimReflect.SetMasterId(_playerManager, masterId);
            MultiSimReflect.SetNextPlayerId(_playerManager, maxId + 1);
            SortAllPlayersByJoinOrder();

            MultiSimLog.Info($"Joined session as player {myId} ({players.Count} existing player(s)).");

            StartCoroutine(ClientReadySequence());
        }

        private IEnumerator ClientReadySequence()
        {
            yield return StartCoroutine(ReadySequence());

            // _ready stays false until after the drain so that live messages arriving
            // during the yield continue to be buffered, preserving TCP arrival order
            // (snapshot first, then live messages).
            yield return null;

            // Apply buffered world state (snapshot first, then live messages, in arrival order).
            List<DataDictionary> pending = new List<DataDictionary>(_pendingWorldMessages);
            _pendingWorldMessages.Clear();
            foreach (DataDictionary envelope in pending)
            {
                ApplyWorldMessage(envelope);
            }

            _ready = true;
        }

        #endregion

        #region Player management

        private void SpawnRemotePlayerWithId(int playerId, string playerName)
        {
            int previousNext = MultiSimReflect.GetNextPlayerId(_playerManager);
            MultiSimReflect.SetNextPlayerId(_playerManager, playerId);
            ClientSimMain.SpawnRemotePlayer(playerName);

            VRCPlayerApi api = _playerManager.GetPlayerByID(playerId);
            if (api == null)
            {
                MultiSimLog.Error($"Failed to spawn remote player with id {playerId}.");
                MultiSimReflect.SetNextPlayerId(_playerManager, previousNext);
                return;
            }

            MultiSimReflect.SetCachedPlayerId(api, playerId);
            MultiSimReflect.SetNextPlayerId(_playerManager, Mathf.Max(previousNext, playerId + 1));

#if VRC_ENABLE_PLAYER_PERSISTENCE
            // ClientSim instantiated this player's VRCPlayerObject copies with the
            // correct id (we forced it above); make them addressable for sync.
            RegisterPlayerObjects(api);
#endif
        }

#if VRC_ENABLE_PLAYER_PERSISTENCE
        /// <summary>
        /// ClientSim instantiates the local player's VRCPlayerObject copies before the
        /// session id is known (default id 1). After the welcome remap, rename the copies
        /// and shift their network ids and owners into the assigned id's range so they
        /// match what every other instance computes for this player.
        /// </summary>
        private void RenumberLocalPlayerObjects(VRCPlayerApi local, int previousId, int newId)
        {
            if (previousId == newId)
            {
                return;
            }

            ClientSimPlayer clientSimPlayer = local.GetClientSimPlayer();
            if (clientSimPlayer == null)
            {
                return;
            }

            int idShift = (newId - previousId) * ClientSimNetworkingUtilities.MaxID;
            string oldSuffix = $" [{previousId}]";
            foreach (GameObject root in clientSimPlayer.PlayerPersistenceRootObjects)
            {
                if (root == null)
                {
                    continue;
                }

                if (root.name.EndsWith(oldSuffix, StringComparison.Ordinal))
                {
                    root.name = root.name.Substring(0, root.name.Length - oldSuffix.Length) + $" [{newId}]";
                }

                foreach (ClientSimNetworkingView view in root.GetComponentsInChildren<ClientSimNetworkingView>(true))
                {
                    int networkId = view.GetNetworkId();
                    if (networkId > 0)
                    {
                        view.SetNetworkId(networkId + idShift);
                    }
                    view.SetPlayerId(newId);
                }

                foreach (IClientSimSyncable syncable in root.GetComponentsInChildren<IClientSimSyncable>(true))
                {
                    if (syncable.GetOwner() == previousId)
                    {
                        syncable.SetOwner(newId);
                    }
                }
            }
        }

        private void RegisterPlayerObjects(VRCPlayerApi player)
        {
            ClientSimPlayer clientSimPlayer = player?.GetClientSimPlayer();
            if (clientSimPlayer == null)
            {
                return;
            }

            foreach (GameObject root in clientSimPlayer.PlayerPersistenceRootObjects)
            {
                _registry.RegisterRuntimeHierarchy(root);
            }
        }
#endif

        private void HandlePlayerJoin(DataDictionary payload)
        {
            int id = (int)payload["id"].Double;
            string name = payload["name"].String;

            if (_playerManager.GetPlayerByID(id) != null)
            {
                return;
            }

            SpawnRemotePlayerWithId(id, name);
            _joinOrder.Add(id);
            SortAllPlayersByJoinOrder();
        }

        private void HandlePlayerLeave(DataDictionary payload)
        {
            int id = (int)payload["id"].Double;
            RemoveRemotePlayer(id);
        }

        private void RemoveRemotePlayer(int playerId)
        {
            VRCPlayerApi api = _playerManager.GetPlayerByID(playerId);
            if (api == null || api.isLocal)
            {
                return;
            }

            ApplyUnseat(playerId, fireEvents: true, warpToExit: false);
            _joinOrder.Remove(playerId);
            ClientSimMain.RemovePlayer(api);
        }

        private void RemoveAllRemotePlayers()
        {
            // Remove non-master players first so master reassignment happens exactly once,
            // when the host (master, first in the list) is removed last.
            List<VRCPlayerApi> remotes = new List<VRCPlayerApi>();
            foreach (VRCPlayerApi api in VRCPlayerApi.AllPlayers)
            {
                if (!api.isLocal)
                {
                    remotes.Add(api);
                }
            }

            VRCPlayerApi master = _playerManager.GetMaster();
            remotes.Sort((a, b) => (a == master ? 1 : 0).CompareTo(b == master ? 1 : 0));
            foreach (VRCPlayerApi api in remotes)
            {
                ApplyUnseat(api.playerId, fireEvents: true, warpToExit: false);
                _joinOrder.Remove(api.playerId);
                ClientSimMain.RemovePlayer(api);
            }
        }

        private void SortAllPlayersByJoinOrder()
        {
            Dictionary<int, int> orderIndex = new Dictionary<int, int>();
            for (int i = 0; i < _joinOrder.Count; i++)
            {
                orderIndex[_joinOrder[i]] = i;
            }

            MultiSimReflect.SortAllPlayers((a, b) =>
            {
                int ia = orderIndex.TryGetValue(a.playerId, out int va) ? va : int.MaxValue;
                int ib = orderIndex.TryGetValue(b.playerId, out int vb) ? vb : int.MaxValue;
                return ia.CompareTo(ib);
            });
        }

        #endregion

        #region Variable sync

        private void SendContinuousUpdates()
        {
            foreach (KeyValuePair<int, ClientSimNetworkIdHolder> pair in _registry.Holders)
            {
                ClientSimNetworkIdHolder holder = pair.Value;
                if (holder == null)
                {
                    continue;
                }

                if (MultiSimRegistry.GetOwnerId(holder.gameObject) != _localPlayerId)
                {
                    continue;
                }

                // IsDirty(null) is always false for manual-sync objects, which only
                // serialize through RequestSerialization.
                if (!holder.IsDirty(null))
                {
                    continue;
                }

                holder.PreEncode(null);
                DataList data = holder.Encode(null);
                holder.PostEncode(null);
                SendVariableSync(pair.Key, data);
            }
        }

        private void SendManualSerialization(UdonBehaviour udonBehaviour)
        {
            GameObject go = udonBehaviour.gameObject;
            if (!_registry.TryGetNetworkId(go, out int networkId) ||
                !_registry.TryGetHolder(networkId, out ClientSimNetworkIdHolder holder))
            {
                return;
            }

            // VRChat ignores RequestSerialization from non-owners.
            if (MultiSimRegistry.GetOwnerId(go) != _localPlayerId)
            {
                return;
            }

            holder.PreEncode(go);
            DataList data = holder.Encode(go);
            holder.PostEncode(go);
            SendVariableSync(networkId, data);
        }

        private void SendVariableSync(int networkId, DataList data)
        {
            if (data == null)
            {
                return;
            }

            DataDictionary payload = new DataDictionary();
            payload["nid"] = networkId;
            payload["data"] = data;
            SendEnvelope("var", payload);
        }

        private void HandleVariableSync(int senderId, DataDictionary payload)
        {
            int networkId = (int)payload["nid"].Double;
            if (!_registry.TryGetHolder(networkId, out ClientSimNetworkIdHolder holder))
            {
                MultiSimLog.Verbose($"Variable sync for unknown network id {networkId}.");
                return;
            }

            int ownerId = MultiSimRegistry.GetOwnerId(holder.gameObject);
            if (ownerId == _localPlayerId)
            {
                // We are authoritative; ignore stale updates from a previous owner.
                return;
            }
            if (senderId != -1 && ownerId != -1 && senderId != ownerId)
            {
                MultiSimLog.Verbose($"Dropping variable sync from {senderId} for object owned by {ownerId}.");
                return;
            }

            if (payload.TryGetValue("data", out DataToken dataToken) && dataToken.TokenType == TokenType.DataList)
            {
                holder.Decode(dataToken.DataList);
            }
        }

        private void HandleSnapshot(DataDictionary payload)
        {
#if VRC_ENABLE_PLAYER_PERSISTENCE
            if (payload.TryGetValue("pdata", out DataToken playerDataToken) &&
                playerDataToken.TokenType == TokenType.DataList)
            {
                DataList playerData = playerDataToken.DataList;
                for (int i = 0; i < playerData.Count; i++)
                {
                    DataDictionary entry = playerData[i].DataDictionary;
                    ApplyPlayerData((int)entry["pid"].Double, entry["json"].String);
                }
            }
#endif

            if (payload.TryGetValue("seats", out DataToken seatsToken) &&
                seatsToken.TokenType == TokenType.DataList)
            {
                DataList seats = seatsToken.DataList;
                for (int i = 0; i < seats.Count; i++)
                {
                    DataDictionary entry = seats[i].DataDictionary;
                    ApplySeat((int)entry["pid"].Double, (int)entry["nid"].Double, fireEvents: true);
                }
            }

            if (!payload.TryGetValue("objs", out DataToken objectsToken) ||
                objectsToken.TokenType != TokenType.DataList)
            {
                return;
            }

            DataList objects = objectsToken.DataList;
            int applied = 0;
            for (int i = 0; i < objects.Count; i++)
            {
                DataDictionary entry = objects[i].DataDictionary;
                int networkId = (int)entry["nid"].Double;
                if (!_registry.TryGetHolder(networkId, out ClientSimNetworkIdHolder holder))
                {
                    continue;
                }

                int owner = (int)entry["owner"].Double;
                ApplyOwnershipLocally(networkId, owner);

                if (entry.TryGetValue("data", out DataToken dataToken) &&
                    dataToken.TokenType == TokenType.DataList &&
                    owner != _localPlayerId)
                {
                    holder.Decode(dataToken.DataList);
                    applied++;
                }
            }

            MultiSimLog.Verbose($"Applied snapshot for {applied} object(s).");
        }

        #endregion

        #region PlayerData sync

#if VRC_ENABLE_PLAYER_PERSISTENCE
        private bool _pendingPlayerDataBroadcast;

        // Fired by ClientSim's dispatcher whenever a storage commits changes (including
        // our own injections into remote stores, which the isLocal check filters out).
        private void OnPlayerDataUpdated(ClientSimOnPlayerDataUpdatedEvent updateEvent)
        {
            if (updateEvent.player == null || !updateEvent.player.isLocal || !_sessionActive)
            {
                return;
            }

            if (!_ready)
            {
                // The initial restore decode can commit before the join finishes;
                // Update() flushes this once the session is ready.
                _pendingPlayerDataBroadcast = true;
                return;
            }

            SendLocalPlayerData();
        }

        private void SendLocalPlayerData()
        {
            _pendingPlayerDataBroadcast = false;
            VRCPlayerApi local = _playerManager.LocalPlayer();
            if (!MultiSimPlayerData.TryGetStorage(local, out ClientSimPlayerDataStorage storage))
            {
                return;
            }

            DataDictionary payload = new DataDictionary();
            payload["pid"] = _localPlayerId;
            payload["json"] = MultiSimPlayerData.Serialize(storage);
            SendEnvelope("pdata", payload);
        }

        private void ApplyPlayerData(int playerId, string json)
        {
            if (playerId == _localPlayerId)
            {
                // We are authoritative for our own data.
                return;
            }

            VRCPlayerApi api = _playerManager.GetPlayerByID(playerId);
            if (api == null || !MultiSimPlayerData.TryGetStorage(api, out ClientSimPlayerDataStorage storage))
            {
                MultiSimLog.Verbose($"PlayerData for unknown player {playerId}.");
                return;
            }

            MultiSimPlayerData.Inject(storage, json);
        }
#endif

        #endregion

        #region Ownership

        private void HandleOwnership(DataDictionary payload)
        {
            int networkId = (int)payload["nid"].Double;
            int ownerId = (int)payload["owner"].Double;
            ApplyOwnershipLocally(networkId, ownerId);
        }

        private void ApplyOwnershipLocally(int networkId, int ownerId)
        {
            if (!_registry.TryGetHolder(networkId, out ClientSimNetworkIdHolder holder))
            {
                return;
            }

            VRCPlayerApi owner = _playerManager.GetPlayerByID(ownerId);
            if (owner == null)
            {
                MultiSimLog.Verbose($"Ownership change to unknown player {ownerId} for network id {networkId}.");
                return;
            }

            _applyingRemoteOwner = true;
            try
            {
                // Direct call: applies locally without re-triggering the Networking._SetOwner hook.
                ClientSimPlayerManager.SetOwner(owner, holder.gameObject);
            }
            finally
            {
                _applyingRemoteOwner = false;
            }
        }

        #endregion

        #region Player positions

        private void SendLocalPlayerPosition()
        {
            // While seated, remotes derive our position from the station itself.
            if (_localSeated)
            {
                return;
            }

            VRCPlayerApi local = _playerManager.LocalPlayer();
            if (local == null)
            {
                return;
            }

            Vector3 position = local.GetPosition();
            Quaternion rotation = local.GetRotation();
            if ((position - _lastSentPosition).sqrMagnitude < 0.0001f &&
                Quaternion.Angle(rotation, _lastSentRotation) < 0.5f)
            {
                return;
            }

            _lastSentPosition = position;
            _lastSentRotation = rotation;

            DataDictionary payload = new DataDictionary();
            payload["pos"] = position.GetJTokenFromVector3();
            payload["rot"] = rotation.GetJTokenFromQuaternion();
            SendEnvelope("pos", payload);
        }

        private void HandlePlayerPosition(int senderId, DataDictionary payload)
        {
            // Seated players are pinned to their station instead.
            if (_seatsByPlayer.ContainsKey(senderId))
            {
                return;
            }

            VRCPlayerApi api = _playerManager.GetPlayerByID(senderId);
            if (api == null || api.isLocal || api.gameObject == null)
            {
                return;
            }

            api.gameObject.transform.SetPositionAndRotation(
                payload["pos"].GetVector3(),
                payload["rot"].GetQuaternion());
        }

        #endregion

        #region Stations

        private void OnLocalPlayerEnteredStation(ClientSimOnPlayerEnteredStationEvent enterEvent)
        {
            GameObject stationObject = enterEvent?.station?.GetStationGameObject();
            if (stationObject == null)
            {
                return;
            }

            _localSeated = true;

            if (!_registry.TryGetNetworkId(stationObject, out int networkId))
            {
                if (_sessionActive)
                {
                    MultiSimLog.Warn($"Station '{stationObject.name}' has no network id; the seat will not be synced. " +
                                     "Put the VRCStation on a GameObject with a networked component (e.g. an UdonBehaviour).");
                }
                return;
            }

            _seatsByPlayer[_localPlayerId] = new SeatInfo
            {
                PlayerId = _localPlayerId,
                NetworkId = networkId,
                StationObject = stationObject,
                Station = stationObject.GetComponent<VRC.SDK3.Components.VRCStation>(),
            };

            if (!_ready || !_sessionActive)
            {
                return;
            }

            DataDictionary payload = new DataDictionary();
            payload["nid"] = networkId;
            SendEnvelope("sit", payload);
        }

        private void OnLocalPlayerExitedStation(ClientSimOnPlayerExitedStationEvent exitEvent)
        {
            _localSeated = false;

            if (!_seatsByPlayer.TryGetValue(_localPlayerId, out SeatInfo seat))
            {
                return;
            }
            _seatsByPlayer.Remove(_localPlayerId);

            // Resume position sync immediately so remotes see the exit spot.
            _lastSentPosition = new Vector3(float.MaxValue, 0f, 0f);
            _nextPlayerPosSend = 0f;

            if (!_ready || !_sessionActive)
            {
                return;
            }

            DataDictionary payload = new DataDictionary();
            payload["nid"] = seat.NetworkId;
            SendEnvelope("unsit", payload);
        }

        private void ApplySeat(int playerId, int networkId, bool fireEvents)
        {
            if (playerId == _localPlayerId || playerId < 0)
            {
                return;
            }

            // A player can only occupy one station at a time.
            ApplyUnseat(playerId, fireEvents: true, warpToExit: false);

            if (!_registry.TryGetGameObject(networkId, out GameObject stationObject))
            {
                MultiSimLog.Verbose($"Seat for unknown network id {networkId}.");
                return;
            }

            var station = stationObject.GetComponent<VRC.SDK3.Components.VRCStation>();
            if (station == null)
            {
                MultiSimLog.Warn($"Seat message for '{stationObject.name}', which has no VRCStation.");
                return;
            }

            SeatInfo seat = new SeatInfo
            {
                PlayerId = playerId,
                NetworkId = networkId,
                StationObject = stationObject,
                Station = station,
            };
            _seatsByPlayer[playerId] = seat;

            VRCPlayerApi player = _playerManager.GetPlayerByID(playerId);
            MultiSimReflect.SetStationOccupant(stationObject, player);
            PinSeat(seat, player);

            if (fireEvents)
            {
                RunStationUdonEvents(seat, player, entered: true);
            }
        }

        private void ApplyUnseat(int playerId, bool fireEvents, bool warpToExit)
        {
            if (!_seatsByPlayer.TryGetValue(playerId, out SeatInfo seat) || playerId == _localPlayerId)
            {
                return;
            }
            _seatsByPlayer.Remove(playerId);

            VRCPlayerApi player = _playerManager.GetPlayerByID(playerId);

            if (seat.StationObject != null)
            {
                VRCPlayerApi occupant = MultiSimReflect.GetStationOccupant(seat.StationObject);
                if (occupant == null || occupant == player || occupant.playerId == playerId)
                {
                    MultiSimReflect.SetStationOccupant(seat.StationObject, null);
                }
            }

            if (warpToExit && player != null && !player.isLocal && player.gameObject != null &&
                seat.Station != null && seat.Station.stationExitPlayerLocation != null)
            {
                Transform exit = seat.Station.stationExitPlayerLocation;
                player.gameObject.transform.SetPositionAndRotation(exit.position, exit.rotation);
            }

            if (fireEvents)
            {
                RunStationUdonEvents(seat, player, entered: false);
            }
        }

        /// <summary>
        /// Fires the station's Udon events for a remote player, using the same event-name
        /// fields the real client uses (UdonSharp sets them to _onStationEntered/_onStationExited).
        /// </summary>
        private void RunStationUdonEvents(SeatInfo seat, VRCPlayerApi player, bool entered)
        {
            if (seat.StationObject == null)
            {
                return;
            }

            string eventName = entered
                ? seat.Station != null ? seat.Station.OnRemotePlayerEnterStation : null
                : seat.Station != null ? seat.Station.OnRemotePlayerExitStation : null;
            if (string.IsNullOrEmpty(eventName))
            {
                eventName = entered ? "_onStationEntered" : "_onStationExited";
            }

            foreach (UdonBehaviour behaviour in seat.StationObject.GetComponents<UdonBehaviour>())
            {
                if (!behaviour.HasDoneStart)
                {
                    continue;
                }
                try
                {
                    // Parameter name gets mangled to the Udon convention, matching
                    // ClientSimUdonHelper's local-player station events.
                    behaviour.RunEvent(eventName, ("Player", player));
                }
                catch (Exception e)
                {
                    MultiSimLog.Error($"Error running station event '{eventName}' on '{behaviour.name}': {e}");
                }
            }
        }

        /// <summary>Keeps seated remote players glued to their station (stations can move).</summary>
        private void PinSeatedRemotePlayers()
        {
            foreach (SeatInfo seat in _seatsByPlayer.Values)
            {
                if (seat.PlayerId == _localPlayerId)
                {
                    continue;
                }
                PinSeat(seat, _playerManager.GetPlayerByID(seat.PlayerId));
            }
        }

        private static void PinSeat(SeatInfo seat, VRCPlayerApi player)
        {
            if (player == null || player.isLocal || player.gameObject == null ||
                seat.Station == null || seat.Station.stationEnterPlayerLocation == null)
            {
                return;
            }

            Transform enter = seat.Station.stationEnterPlayerLocation;
            player.gameObject.transform.SetPositionAndRotation(enter.position, enter.rotation);
        }

        #endregion

        #region Custom network events

        private void ForwardCustomNetworkEvent(
            IUdonEventReceiver receiver, NetworkEventTarget target, string eventName, Memory<object> parameters)
        {
            if (!_ready || !_sessionActive)
            {
                return;
            }

            if (target == NetworkEventTarget.Self)
            {
                return;
            }

            if (!(receiver is UdonBehaviour udonBehaviour) || udonBehaviour == null)
            {
                return;
            }

            // Mirror ClientSim's validation: it already logged errors for these cases.
            if (udonBehaviour.SyncMethod == Networking.SyncType.None ||
                !udonBehaviour.TryGetEntrypointHashFromName(eventName, out _))
            {
                return;
            }

            NetworkCallingEntrypointMetadata metadata = udonBehaviour.GetNetworkCallingMetadata(eventName);
            if (metadata == null && (eventName.StartsWith("_") || parameters.Length > 0))
            {
                return;
            }

            if (!_registry.TryGetNetworkId(udonBehaviour.gameObject, out int networkId))
            {
                MultiSimLog.Warn($"SendCustomNetworkEvent('{eventName}') on '{udonBehaviour.name}' " +
                                 "cannot be forwarded: the object has no network id (runtime-instantiated objects are not synced).");
                return;
            }

            int behaviourIndex = -1;
            if (metadata != null)
            {
                UdonBehaviour[] behaviours = udonBehaviour.gameObject.GetComponents<UdonBehaviour>();
                behaviourIndex = Array.IndexOf(behaviours, udonBehaviour);
            }

            DataDictionary payload = new DataDictionary();
            payload["nid"] = networkId;
            payload["ub"] = behaviourIndex;
            payload["name"] = eventName;
            payload["tgt"] = (int)target;

            DataList parameterList = new DataList();
            for (int i = 0; i < parameters.Length; i++)
            {
                parameterList.Add(MultiSimJson.ObjectToTypedToken(parameters.Span[i]));
            }
            payload["prm"] = parameterList;

            SendEnvelope("event", payload);
        }

        private void HandleNetworkEvent(int senderId, DataDictionary payload)
        {
            int networkId = (int)payload["nid"].Double;
            if (!_registry.TryGetHolder(networkId, out ClientSimNetworkIdHolder holder))
            {
                MultiSimLog.Verbose($"Network event for unknown network id {networkId}.");
                return;
            }

            GameObject go = holder.gameObject;
            NetworkEventTarget target = (NetworkEventTarget)(int)payload["tgt"].Double;
            if (target == NetworkEventTarget.Owner && MultiSimRegistry.GetOwnerId(go) != _localPlayerId)
            {
                return;
            }

            string eventName = payload["name"].String;
            int behaviourIndex = (int)payload["ub"].Double;
            VRCPlayerApi sender = _playerManager.GetPlayerByID(senderId);

            object[] parameters = Array.Empty<object>();
            if (payload.TryGetValue("prm", out DataToken paramToken) && paramToken.TokenType == TokenType.DataList)
            {
                DataList list = paramToken.DataList;
                parameters = new object[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    parameters[i] = MultiSimJson.TypedTokenToObject(list[i]);
                }
            }

            if (behaviourIndex >= 0)
            {
                UdonBehaviour[] behaviours = go.GetComponents<UdonBehaviour>();
                if (behaviourIndex >= behaviours.Length)
                {
                    MultiSimLog.Warn($"Network event '{eventName}': behaviour index {behaviourIndex} out of range on '{go.name}'.");
                    return;
                }
                RunNetworkEvent(sender, behaviours[behaviourIndex], eventName, parameters);
            }
            else
            {
                // Legacy event: fan out to every syncable UdonBehaviour on the object.
                foreach (UdonBehaviour behaviour in go.GetComponents<UdonBehaviour>())
                {
                    if (behaviour.SyncMethod == Networking.SyncType.None)
                    {
                        continue;
                    }
                    RunNetworkEvent(sender, behaviour, eventName, Array.Empty<object>());
                }
            }
        }

        private void RunNetworkEvent(VRCPlayerApi sender, UdonBehaviour behaviour, string eventName, object[] parameters)
        {
            NetworkCallingEntrypointMetadata metadata = behaviour.GetNetworkCallingMetadata(eventName);
            int parameterCount = metadata?.Parameters != null ? metadata.Parameters.Length : 0;

            object P(int i) => i < parameters.Length ? parameters[i] : null;
            string N(int i) => metadata.Parameters[i].Name;

            try
            {
                MultiSimReflect.SetNetworkCallingContext(true, sender);
                switch (parameterCount)
                {
                    case 0:
                        behaviour.RunEventAdvanced(eventName, canRunBeforeStart: true);
                        break;
                    case 1:
                        behaviour.RunEventAdvanced<object>(eventName, false, true,
                            (N(0), P(0)));
                        break;
                    case 2:
                        behaviour.RunEventAdvanced<object, object>(eventName, false, true,
                            (N(0), P(0)), (N(1), P(1)));
                        break;
                    case 3:
                        behaviour.RunEventAdvanced<object, object, object>(eventName, false, true,
                            (N(0), P(0)), (N(1), P(1)), (N(2), P(2)));
                        break;
                    case 4:
                        behaviour.RunEventAdvanced<object, object, object, object>(eventName, false, true,
                            (N(0), P(0)), (N(1), P(1)), (N(2), P(2)), (N(3), P(3)));
                        break;
                    case 5:
                        behaviour.RunEventAdvanced<object, object, object, object, object>(eventName, false, true,
                            (N(0), P(0)), (N(1), P(1)), (N(2), P(2)), (N(3), P(3)), (N(4), P(4)));
                        break;
                    case 6:
                        behaviour.RunEventAdvanced<object, object, object, object, object, object>(eventName, false, true,
                            (N(0), P(0)), (N(1), P(1)), (N(2), P(2)), (N(3), P(3)), (N(4), P(4)), (N(5), P(5)));
                        break;
                    case 7:
                        behaviour.RunEventAdvanced<object, object, object, object, object, object, object>(eventName, false, true,
                            (N(0), P(0)), (N(1), P(1)), (N(2), P(2)), (N(3), P(3)), (N(4), P(4)), (N(5), P(5)), (N(6), P(6)));
                        break;
                    case 8:
                        behaviour.RunEventAdvanced<object, object, object, object, object, object, object, object>(eventName, false, true,
                            (N(0), P(0)), (N(1), P(1)), (N(2), P(2)), (N(3), P(3)), (N(4), P(4)), (N(5), P(5)), (N(6), P(6)), (N(7), P(7)));
                        break;
                }
            }
            catch (Exception e)
            {
                MultiSimLog.Error($"Error running network event '{eventName}' on '{behaviour.name}': {e}");
            }
            finally
            {
                MultiSimReflect.SetNetworkCallingContext(false, null);
            }
        }

        #endregion

        #region SDK hooks

        private void InstallHooks()
        {
            UdonBehaviour.RequestSerializationHook += OnRequestSerialization;
            Networking._SetOwner += OnSetOwnerCalled;
            VRCPlayerApi._TakeOwnership += OnSetOwnerCalled;
            // ClientSim's GetUniqueName returns GetInstanceID(), which differs per editor
            // process and breaks cross-instance identity (e.g. QvPen derives pen ids from
            // it). Registering after ClientSim makes this multicast handler's return win.
            Networking._GetUniqueName += GetDeterministicUniqueName;

            MethodInfo handler = typeof(MultiSimCore).GetMethod(
                nameof(OnSendCustomNetworkEvent), BindingFlags.Static | BindingFlags.NonPublic);
            _sendEventHandler = MultiSimReflect.AddSendCustomNetworkEventHandler(handler);

            _hooksInstalled = true;
        }

        private void RemoveHooks()
        {
            if (!_hooksInstalled)
            {
                return;
            }

            UdonBehaviour.RequestSerializationHook -= OnRequestSerialization;
            Networking._SetOwner -= OnSetOwnerCalled;
            VRCPlayerApi._TakeOwnership -= OnSetOwnerCalled;
            Networking._GetUniqueName -= GetDeterministicUniqueName;
            MultiSimReflect.RemoveSendCustomNetworkEventHandler(_sendEventHandler);
            _sendEventHandler = null;
            _hooksInstalled = false;
        }

        /// <summary>
        /// Cross-instance stable replacement for GetUniqueName. Uses the full hierarchy
        /// path with sibling indices, which is identical in both editors for scene
        /// objects (the scenes are clones of each other).
        /// </summary>
        private static string GetDeterministicUniqueName(GameObject obj)
        {
            if (obj == null)
            {
                return string.Empty;
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            Transform t = obj.transform;
            while (t != null)
            {
                sb.Insert(0, $"/{t.GetSiblingIndex()}:{t.name}");
                t = t.parent;
            }
            return sb.ToString();
        }

        private void OnRequestSerialization(UdonBehaviour udonBehaviour)
        {
            if (!_ready || !_sessionActive || udonBehaviour == null)
            {
                return;
            }

            if (!_manualSerializationQueue.Contains(udonBehaviour))
            {
                _manualSerializationQueue.Add(udonBehaviour);
            }
        }

        // Runs after ClientSim's own SetOwner handler (registration order), so local
        // state is already updated when this fires.
        private void OnSetOwnerCalled(VRCPlayerApi player, GameObject obj)
        {
            if (!_ready || !_sessionActive || _applyingRemoteOwner || player == null || obj == null)
            {
                return;
            }

            if (!_registry.TryGetNetworkId(obj, out int networkId))
            {
                return;
            }

            DataDictionary payload = new DataDictionary();
            payload["nid"] = networkId;
            payload["owner"] = player.playerId;
            SendEnvelope("owner", payload);
        }

        private static void OnSendCustomNetworkEvent(
            IUdonEventReceiver receiver, NetworkEventTarget target, string eventName, Memory<object> parameters)
        {
            if (_instance != null)
            {
                _instance.ForwardCustomNetworkEvent(receiver, target, eventName, parameters);
            }
        }

        #endregion

        #region Message sending

        private DataDictionary BuildEnvelope(string type, DataDictionary payload)
        {
            DataDictionary envelope = new DataDictionary();
            envelope["t"] = type;
            envelope["s"] = _localPlayerId;
            envelope["p"] = payload;
            return envelope;
        }

        private void SendEnvelope(string type, DataDictionary payload)
        {
            string json = MultiSimJson.Serialize(BuildEnvelope(type, payload));
            if (json == null)
            {
                return;
            }
            MultiSimLog.Verbose($"send '{type}' ({json.Length} chars): {json.Substring(0, Mathf.Min(json.Length, 220))}");

            if (_transport.IsHost)
            {
                _transport.Broadcast(json);
            }
            else
            {
                _transport.SendToHost(json);
            }
        }

        private void SendEnvelopeTo(int connectionId, string type, DataDictionary payload)
        {
            string json = MultiSimJson.Serialize(BuildEnvelope(type, payload));
            if (json != null)
            {
                _transport.Send(connectionId, json);
            }
        }

        private void BroadcastEnvelope(string type, DataDictionary payload, int exceptConnectionId)
        {
            string json = MultiSimJson.Serialize(BuildEnvelope(type, payload));
            if (json != null)
            {
                _transport.Broadcast(json, exceptConnectionId);
            }
        }

        #endregion
    }
}
#endif
