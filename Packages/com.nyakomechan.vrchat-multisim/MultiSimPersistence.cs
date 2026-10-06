#if UNITY_EDITOR && VRC_ENABLE_PLAYER_PERSISTENCE
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.SDK3.ClientSim.Persistence;

namespace MultiSim
{
    /// <summary>
    /// Keeps persistence files stable across sessions.
    ///
    /// ClientSim saves PlayerData/PlayerObject state to files keyed by the session
    /// playerId ("PlayerData_{playerId}_{scene}.json"). A MultiSim client's id depends
    /// on join order, so the same editor would read and write different files from one
    /// session to the next and restore would silently break. Each editor already has
    /// its own ClientSimStorage folder (the ParrelSync clone is a separate project
    /// root), so the id in the filename carries no information — this class maps a
    /// per-editor stable file ("..._local_{scene}.json") onto whatever id the current
    /// session assigned: copy stable → session file before ClientSim decodes, copy
    /// session file → stable when play ends.
    /// </summary>
    internal static class MultiSimPersistence
    {
        private const string StableToken = "local";

        private static bool _staged;

        /// <summary>
        /// Makes the stable files readable under this session's id. Must run before
        /// ClientSim decodes, i.e. before the local player's OnPlayerJoined dispatch
        /// (MultiSim holds join events until ReadySequence).
        /// If no stable file exists yet, the session-id file is left as-is so existing
        /// saves keep working; SaveBack then establishes the stable file for next time.
        /// </summary>
        public static void StageForSession(int sessionPlayerId)
        {
            CopyIfExists(PlayerDataPath(StableToken), PlayerDataPath(sessionPlayerId.ToString()));
            CopyIfExists(PlayerObjectPath(StableToken), PlayerObjectPath(sessionPlayerId.ToString()));
            _staged = true;
        }

        /// <summary>Adopts this session's saves as the stable files. Call at play exit.</summary>
        public static void SaveBack(int sessionPlayerId)
        {
            if (!_staged)
            {
                return;
            }
            _staged = false;

            CopyIfExists(PlayerDataPath(sessionPlayerId.ToString()), PlayerDataPath(StableToken));
            CopyIfExists(PlayerObjectPath(sessionPlayerId.ToString()), PlayerObjectPath(StableToken));
        }

        private static void CopyIfExists(string source, string destination)
        {
            try
            {
                if (File.Exists(source))
                {
                    File.Copy(source, destination, overwrite: true);
                }
            }
            catch (IOException e)
            {
                MultiSimLog.Warn($"Persistence file copy failed ({source} -> {destination}): {e.Message}");
            }
        }

        // Filename formats mirror ClientSimPlayerDataStorage.PlayerDataFilePath and
        // ClientSimPlayerObjectStorage.PlayerDataFilePath (internal). If an SDK update
        // changes those, update these too.
        private static string PlayerDataPath(string idToken)
        {
            return Path.Combine(EnsureFolder(ClientSimPlayerDataStorage.PlayerDataFolder),
                $"PlayerData_{idToken}_{SceneManager.GetActiveScene().name}.json");
        }

        private static string PlayerObjectPath(string idToken)
        {
            return Path.Combine(EnsureFolder(ClientSimPlayerObjectStorage.PlayerObjectsFolder),
                $"PlayerObject_{idToken}_{SceneManager.GetActiveScene().name}.json");
        }

        private static string EnsureFolder(string relativeFolder)
        {
            string path = Path.Combine(Path.GetDirectoryName(Application.dataPath), relativeFolder);
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
            return path;
        }
    }
}
#endif
