
using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;

namespace VRC.Examples
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class WorldQualitySettings : UdonSharpBehaviour
    {
        public ShadowmaskMode shadowmaskMode = ShadowmaskMode.Shadowmask;
        public bool overrideRealtimeReflectionProbes;
        public bool realtimeReflectionsPc = true;
        public bool realtimeReflectionsMobile;
        public bool overrideShadowDistance;
        public float shadowDistanceHigh = 150;
        public float shadowDistanceMedium = 75;
        public float shadowDistanceLow = 75;
        public float shadowDistanceMobile = 50;
        
        void Start()
        {
            VRCQualitySettings.ShadowmaskMode = shadowmaskMode;
            if (overrideRealtimeReflectionProbes)
            {
                #if UNITY_STANDALONE
                VRCQualitySettings.RealtimeReflectionProbes = realtimeReflectionsPc;
                #else
                VRCQualitySettings.RealtimeReflectionProbes = realtimeReflectionsMobile;
                #endif
            }

            if (overrideShadowDistance)
            {
                VRCQualitySettings.SetShadowDistance(shadowDistanceLow, shadowDistanceMedium, shadowDistanceHigh, shadowDistanceMobile);
            }
        }
    }
}
