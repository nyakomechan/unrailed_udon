using UdonSharpEditor;
using UnityEditor;
using UnityEngine;

namespace VRC.Examples
{
    [CustomEditor(typeof(WorldQualitySettings))]
    public class WorldQualitySettingsEditor: UnityEditor.Editor
    {
        private SerializedProperty _shadowmaskMode;
        private SerializedProperty _overrideRealtimeReflectionProbes;
        private SerializedProperty _realtimeReflectionsPc;
        private SerializedProperty _realtimeReflectionsMobile;
        private SerializedProperty _realtimeReflectionsBehaviour;
        private SerializedProperty _overrideShadowDistance;
        private SerializedProperty _shadowDistanceHigh;
        private SerializedProperty _shadowDistanceMedium;
        private SerializedProperty _shadowDistanceLow;
        private SerializedProperty _shadowDistanceMobile;

        private void OnEnable()
        {
            _shadowmaskMode = serializedObject.FindProperty("shadowmaskMode");
            _overrideRealtimeReflectionProbes = serializedObject.FindProperty("overrideRealtimeReflectionProbes");
            _realtimeReflectionsPc = serializedObject.FindProperty("realtimeReflectionsPc");
            _realtimeReflectionsMobile = serializedObject.FindProperty("realtimeReflectionsMobile");
            _overrideShadowDistance = serializedObject.FindProperty("overrideShadowDistance");
            _shadowDistanceHigh = serializedObject.FindProperty("shadowDistanceHigh");
            _shadowDistanceMedium = serializedObject.FindProperty("shadowDistanceMedium");
            _shadowDistanceLow = serializedObject.FindProperty("shadowDistanceLow");
            _shadowDistanceMobile = serializedObject.FindProperty("shadowDistanceMobile");
        }
        
        public override void OnInspectorGUI()
        {
            if (UdonSharpGUI.DrawDefaultUdonSharpBehaviourHeader(target)) return;

            serializedObject.Update();
            
            using var c = new EditorGUI.ChangeCheckScope();
            
            EditorGUILayout.LabelField("Realtime Reflection Probes", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_overrideRealtimeReflectionProbes, new GUIContent("Override"));
            
            if (_overrideRealtimeReflectionProbes.boolValue)
            {
                EditorGUILayout.PropertyField(_realtimeReflectionsPc, new GUIContent("Enable on PC"));
                EditorGUILayout.PropertyField(_realtimeReflectionsMobile, new GUIContent("Enable on Mobile"));
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Shadows", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_shadowmaskMode);
            EditorGUILayout.PropertyField(_overrideShadowDistance);
            if (_overrideShadowDistance.boolValue)
            {
                EditorGUILayout.PropertyField(_shadowDistanceHigh, new GUIContent("High"));
                EditorGUILayout.PropertyField(_shadowDistanceMedium, new GUIContent("Medium"));
                EditorGUILayout.PropertyField(_shadowDistanceLow, new GUIContent("Low"));
                EditorGUILayout.PropertyField(_shadowDistanceMobile, new GUIContent("Mobile"));
                EditorGUILayout.HelpBox("While you can set the shadow distance for mobile, all shadows are currently disabled on those platforms and will not appear in-game", MessageType.Info);
            }

            if (c.changed)
            {
                serializedObject.ApplyModifiedProperties();
            }
        }
    }
}