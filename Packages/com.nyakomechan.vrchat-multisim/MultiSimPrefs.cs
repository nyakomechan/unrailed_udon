#if UNITY_EDITOR
using UnityEditor;

namespace MultiSim
{
    /// <summary>
    /// Settings for the multiplayer simulation. Stored in EditorPrefs, which is shared between
    /// the original project and its ParrelSync clones, so a single toggle controls all instances.
    /// The host/client role is derived automatically from ParrelSync (original = host, clone = client).
    /// </summary>
    internal static class MultiSimPrefs
    {
        private const string EnabledKey = "MultiSim.Enabled";
        private const string PortKey = "MultiSim.Port";
        private const string VerboseKey = "MultiSim.Verbose";

        public const int ProtocolVersion = 2;

        public static bool Enabled
        {
            get => EditorPrefs.GetBool(EnabledKey, false);
            set => EditorPrefs.SetBool(EnabledKey, value);
        }

        public static int Port
        {
            get => EditorPrefs.GetInt(PortKey, 24685);
            set => EditorPrefs.SetInt(PortKey, value);
        }

        public static bool VerboseLogging
        {
            get => EditorPrefs.GetBool(VerboseKey, false);
            set => EditorPrefs.SetBool(VerboseKey, value);
        }

        /// <summary>Seconds a client waits for the host before falling back to single player.</summary>
        public const float ConnectTimeout = 15f;

        /// <summary>Interval for continuous variable sync and object sync scans.</summary>
        public const float ContinuousSyncInterval = 0.1f;

        /// <summary>Interval for local player position broadcasts.</summary>
        public const float PlayerPosInterval = 0.1f;

        /// <summary>Whether ParrelSync is installed (driven by asmdef versionDefines).</summary>
        public static bool HasParrelSync
        {
            get
            {
#if MULTISIM_PARRELSYNC
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>True when this editor is a ParrelSync clone (= joins as a client).</summary>
        public static bool IsCloneInstance()
        {
#if MULTISIM_PARRELSYNC
            return ParrelSync.ClonesManager.IsClone();
#else
            return false;
#endif
        }

        /// <summary>The ParrelSync clone argument, used for the default player name.</summary>
        public static string CloneArgument()
        {
#if MULTISIM_PARRELSYNC
            return ParrelSync.ClonesManager.GetArgument();
#else
            return "";
#endif
        }
    }
}
#endif
