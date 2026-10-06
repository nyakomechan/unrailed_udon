#if UNITY_EDITOR
using UnityEngine;

namespace MultiSim
{
    internal static class MultiSimLog
    {
        private const string Prefix = "[<color=#4FC3F7>MultiSim</color>] ";

        public static void Info(string message)
        {
            Debug.Log(Prefix + message);
        }

        public static void Verbose(string message)
        {
            if (MultiSimPrefs.VerboseLogging)
            {
                Debug.Log(Prefix + message);
            }
        }

        public static void Warn(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        public static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
#endif
