#if UNITY_EDITOR && VRC_ENABLE_PLAYER_PERSISTENCE
using System.Collections.Generic;
using Newtonsoft.Json;
using VRC.SDK3.ClientSim;
using VRC.SDK3.ClientSim.Persistence;
using VRC.SDK3.Persistence;
using VRC.SDKBase;

namespace MultiSim
{
    /// <summary>
    /// Cross-editor PlayerData transport helpers.
    ///
    /// ClientSim simulates PlayerData per editor: setters only ever write the local
    /// player's ClientSimPlayerDataStorage, and remote players' storages stay empty.
    /// MultiSim serializes the local store with the same Newtonsoft converter ClientSim
    /// uses for its save files (round-trips all supported types), ships it as a "pdata"
    /// message, and injects it into the matching remote player's storage on the other
    /// editors through the storage's public typed setters — so OnPlayerDataUpdated fires
    /// there with the correct player, and dedup keeps unchanged keys silent.
    /// </summary>
    internal static class MultiSimPlayerData
    {
        private static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            // Matches ClientSimPlayerDataStorage.Encode's serialization of the same dict.
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        };

        public static bool TryGetStorage(VRCPlayerApi player, out ClientSimPlayerDataStorage storage)
        {
            storage = null;
            ClientSimPlayer clientSimPlayer = player?.GetClientSimPlayer();
            if (clientSimPlayer == null)
            {
                return false;
            }

            storage = MultiSimReflect.GetPlayerDataStorage(clientSimPlayer);
            return storage != null;
        }

        public static string Serialize(ClientSimPlayerDataStorage storage)
        {
            Dictionary<string, ClientSimPlayerDataPair> data = MultiSimReflect.GetPlayerDataDictionary(storage);
            return JsonConvert.SerializeObject(data, Formatting.None, Settings);
        }

        /// <summary>
        /// Writes the serialized entries into the given (remote) player's storage.
        /// Uses the storage's own typed setters so its LateUpdate commit raises
        /// OnPlayerDataUpdated; unchanged values are deduped and stay silent.
        /// </summary>
        public static void Inject(ClientSimPlayerDataStorage storage, string json)
        {
            Dictionary<string, ClientSimPlayerDataPair> data;
            try
            {
                data = JsonConvert.DeserializeObject<Dictionary<string, ClientSimPlayerDataPair>>(json, Settings);
            }
            catch (JsonException e)
            {
                MultiSimLog.Warn($"Failed to parse incoming PlayerData: {e.Message}");
                return;
            }

            if (data == null)
            {
                return;
            }

            bool anyChanged = false;
            foreach (ClientSimPlayerDataPair pair in data.Values)
            {
                if (Apply(storage, pair) != PlayerData.State.Unchanged)
                {
                    anyChanged = true;
                }
            }

            if (anyChanged)
            {
                MultiSimReflect.FlushPlayerDataChanges(storage);
            }
        }

        private static PlayerData.State Apply(ClientSimPlayerDataStorage storage, ClientSimPlayerDataPair pair)
        {
            string key = pair.Key;
            ClientSimPlayerDataTypeUnion value = pair.Value;
            System.DateTime updated = pair.LastUpdated;

            // Same dispatch as ClientSim's own ClientSimPlayerDataWindow.SetData.
            // Note ClientSim's naming inversion: WrappedByte holds an sbyte and
            // WrappedUByte holds a byte.
            switch (value.Type)
            {
                case ClientSimPlayerDataType.Vector2:
                    return storage.SetVector2(key, value.AsVector2(), updated, false, false);
                case ClientSimPlayerDataType.Vector3:
                    return storage.SetVector3(key, value.AsVector3(), updated, false, false);
                case ClientSimPlayerDataType.Vector4:
                    return storage.SetVector4(key, value.AsVector4(), updated, false, false);
                case ClientSimPlayerDataType.Quaternion:
                    return storage.SetQuaternion(key, value.AsQuaternion(), updated, false, false);
                case ClientSimPlayerDataType.Color:
                    return storage.SetColor(key, value.AsColor(), updated, false, false);
                case ClientSimPlayerDataType.Color32:
                    return storage.SetColor32(key, value.AsColor32(), updated, false, false);
                case ClientSimPlayerDataType.WrappedString:
                    return storage.SetString(key, value.AsWrappedString(), updated, false, false);
                case ClientSimPlayerDataType.WrappedShort:
                    return storage.SetShort(key, value.AsWrappedShort(), updated, false, false);
                case ClientSimPlayerDataType.WrappedInt:
                    return storage.SetInt(key, value.AsWrappedInt(), updated, false, false);
                case ClientSimPlayerDataType.WrappedFloat:
                    return storage.SetFloat(key, value.AsWrappedFloat(), updated, false, false);
                case ClientSimPlayerDataType.WrappedBool:
                    return storage.SetBool(key, value.AsWrappedBool(), updated, false, false);
                case ClientSimPlayerDataType.WrappedByte:
                    return storage.SetSByte(key, value.AsWrappedSByte(), updated, false, false);
                case ClientSimPlayerDataType.WrappedUByte:
                    return storage.SetByte(key, value.AsWrappedUByte(), updated, false, false);
                case ClientSimPlayerDataType.WrappedBytes:
                    return storage.SetBytes(key, value.AsWrappedBytes(), updated, false, false);
                case ClientSimPlayerDataType.WrappedUShort:
                    return storage.SetUShort(key, value.AsWrappedUShort(), updated, false, false);
                case ClientSimPlayerDataType.WrappedUInt:
                    return storage.SetUInt(key, value.AsWrappedUInt(), updated, false, false);
                case ClientSimPlayerDataType.WrappedULong:
                    return storage.SetULong(key, value.AsWrappedULong(), updated, false, false);
                case ClientSimPlayerDataType.WrappedDouble:
                    return storage.SetDouble(key, value.AsWrappedDouble(), updated, false, false);
                case ClientSimPlayerDataType.WrappedLong:
                    return storage.SetLong(key, value.AsWrappedLong(), updated, false, false);
                default:
                    MultiSimLog.Warn($"Unsupported PlayerData type '{value.Type}' for key '{key}'.");
                    return PlayerData.State.Unchanged;
            }
        }
    }
}
#endif
