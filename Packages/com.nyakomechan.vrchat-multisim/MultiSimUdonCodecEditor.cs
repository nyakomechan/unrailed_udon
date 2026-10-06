#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using VRC.SDK3.ClientSim.Editor.VisualElements.EncodeDecodeEditors;
using VRC.SDK3.Data;
using VRC.Udon;
using VRC.Udon.Common.Interfaces;

namespace MultiSim
{
    /// <summary>
    /// Inspector renderer for UdonBehaviour data inside ClientSimNetworkIdHolder's editor.
    ///
    /// ClientSim's own ClientSimUdonEncodeDecodeEditor assumes the original codec's flat
    /// token layout; MultiSim's typed tokens ({"t","v"}) make its conversion return null,
    /// which crashes FieldFactory.GenerateField. This editor understands both formats and
    /// renders read-only labels, so it never feeds FieldFactory unsupported values.
    /// </summary>
    internal class MultiSimUdonCodecEditor : IClientSimEncodeDecoderEditor
    {
        [InitializeOnLoadMethod]
        private static void RegisterOnLoad()
        {
            if (MultiSimPrefs.Enabled)
            {
                TryRegister();
            }
        }

        /// <summary>
        /// Replaces ClientSim's UdonBehaviour entry in the (internal) editor renderer map.
        /// No-ops when player persistence is disabled (the map's type doesn't exist then).
        /// </summary>
        internal static void TryRegister()
        {
            Type holderElementType = Type.GetType(
                "VRC.SDK3.ClientSim.Editor.VisualElements.ClientSimNetworkHolderInstanceElement, VRC.ClientSim.Editor");
            FieldInfo dictField = holderElementType?.GetField(
                "encodeDecoders", BindingFlags.Static | BindingFlags.NonPublic);
            if (dictField?.GetValue(null) is Dictionary<string, IClientSimEncodeDecoderEditor> editors)
            {
                editors[typeof(UdonBehaviour).FullName] = new MultiSimUdonCodecEditor();
            }
        }

        public VisualElement GenerateFields(MonoBehaviour component, DataDictionary data)
        {
            VisualElement dataElement = new VisualElement();
            UdonBehaviour udonBehaviour = component as UdonBehaviour;
            if (udonBehaviour == null || udonBehaviour.SyncMetadataTable == null || data == null)
            {
                return dataElement;
            }

            foreach (IUdonSyncMetadata metadata in udonBehaviour.SyncMetadataTable.GetAllSyncMetadata())
            {
                if (!data.TryGetValue(metadata.Name, out DataToken token))
                {
                    continue;
                }

                Label label = new Label(FormatEntry(metadata.Name, token));
                label.name = metadata.Name;
                dataElement.Add(label);
            }
            return dataElement;
        }

        public void UpdateFields(MonoBehaviour component, VisualElement dataElement, DataDictionary data)
        {
            UdonBehaviour udonBehaviour = component as UdonBehaviour;
            if (udonBehaviour == null || udonBehaviour.SyncMetadataTable == null ||
                dataElement == null || data == null)
            {
                return;
            }

            int index = 0;
            foreach (IUdonSyncMetadata metadata in udonBehaviour.SyncMetadataTable.GetAllSyncMetadata())
            {
                if (!data.TryGetValue(metadata.Name, out DataToken token))
                {
                    continue;
                }

                if (index >= dataElement.childCount)
                {
                    break;
                }

                if (dataElement[index] is Label label)
                {
                    label.text = FormatEntry(metadata.Name, token);
                }
                index++;
            }
        }

        private static string FormatEntry(string name, DataToken token)
        {
            return $"{name}: {FormatValue(TokenToValue(token))}";
        }

        private static object TokenToValue(DataToken token)
        {
            // MultiSim typed token; anything else is shown raw (covers ClientSim's
            // original flat format if this editor is ever active without MultiSim).
            if (token.TokenType == TokenType.DataDictionary && token.DataDictionary.ContainsKey("t"))
            {
                return MultiSimJson.TypedTokenToObject(token);
            }
            return token;
        }

        private static string FormatValue(object value)
        {
            if (value == null)
            {
                return "null";
            }

            if (value is Array array)
            {
                const int maxShown = 8;
                StringBuilder sb = new StringBuilder();
                sb.Append(array.Length).Append(" items [");
                int shown = Math.Min(array.Length, maxShown);
                for (int i = 0; i < shown; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }
                    sb.Append(array.GetValue(i));
                }
                if (array.Length > shown)
                {
                    sb.Append(", …");
                }
                sb.Append(']');
                return sb.ToString();
            }

            return value.ToString();
        }
    }
}
#endif
